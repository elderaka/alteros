using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AlterOS.Activities;
using AlterOS.Authoring;
using AlterOS.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Builds an isolated, pinned W5 proof release from an explicit exported SQL snapshot.</summary>
public static class PrepareW5ProofRelease
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);

    private sealed class Source
    {
        public string id, kind, path, operation;
        public byte[] bytes;
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args != null && args.Length == 3)
            {
                Run(args[0], args[1], args[2]);
                return 0;
            }
            if (args != null && args.Length == 10 && args[0] == "integrate")
            {
                IntegrateSharedStore(args.Skip(1).ToArray());
                return 0;
            }
            else
                throw new InvalidOperationException("Usage: PrepareW5ProofRelease <authored-export> <unique-temp-root> <project-root>");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run(string exportRoot, string outputRoot, string projectRoot)
    {
        exportRoot = Path.GetFullPath(exportRoot);
        outputRoot = Path.GetFullPath(outputRoot);
        projectRoot = Path.GetFullPath(projectRoot);
        if (Environment.OSVersion.Platform == PlatformID.Win32NT && !SetDllDirectory(AppDomain.CurrentDomain.BaseDirectory))
            throw new InvalidOperationException("Could not add the isolated runner directory to the Windows native DLL search path.");
        string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new InvalidOperationException("Choose a new, non-existing output directory under the system temporary folder.");
        Directory.CreateDirectory(outputRoot);
        string authoringPath = Path.Combine(outputRoot, "authoring.sqlite3");
        string releasePath = Path.Combine(outputRoot, "w5-proof-release.sqlite3");
        string snapshotDirectoryRoot = Path.Combine(outputRoot, "snapshot");
        string semanticRoot = Path.Combine(outputRoot, "gte");
        Directory.CreateDirectory(semanticRoot);
        Directory.CreateDirectory(snapshotDirectoryRoot);

        AuthoringResult<AuthoringRepository> created = AuthoringRepository.CreateNew(authoringPath);
        Require(created, "create isolated authoring store");
        string storeId;
        AuthoringSnapshot snapshot;
        List<string> selectedIds;
        using (AuthoringRepository repository = created.value)
        {
            AuthoringResult<AuthoringStoreInfo> info = repository.ReadStoreInfo();
            Require(info, "read isolated authoring store identity");
            storeId = info.value.storeId;
            AuthoringResult<AuthoringImportRequest> importRequest = AuthoringJsonExporter.CreateImportRequest(
                exportRoot, storeId, info.value.storeRevision, "w5-retained-source-import");
            Require(importRequest, "prepare retained source snapshot import");
            AuthoringResult<AuthoringImportReceipt> imported = AuthoringJsonImporter.ImportOnce(repository, importRequest.value);
            Require(imported, "import retained source snapshot");

            AuthoringResult<AuthoringDocumentPage> page = repository.List("", "", 100);
            Require(page, "list retained source snapshot");
            var docs = new List<AuthoringDocumentSummary>(page.value.documents);
            while (!string.IsNullOrEmpty(page.value.nextCursor))
            {
                page = repository.List("", page.value.nextCursor, 100);
                Require(page, "page retained source snapshot");
                docs.AddRange(page.value.documents);
            }
            selectedIds = docs.Select(item => item.documentId).ToList();

            Source greeting = EditGreeting(repository, docs);
            Save(repository, greeting);

            List<Source> fixtures = CreateReusableFixtureSources(projectRoot, storeId, repository);
            foreach (Source source in fixtures)
            {
                Save(repository, source);
                selectedIds.Add(source.id);
            }

            AuthoringResult<AuthoringBuildSet> set = repository.SaveBuildSet(new AuthoringBuildSetRequest
            {
                name = "default", documentIds = selectedIds, expectedHash = "", operationId = "w5-proof-default-buildset"
            });
            Require(set, "save explicit W5 proof build set");
            AuthoringResult<AuthoringSnapshot> captured = repository.ReadSnapshot(set.value.documentIds);
            Require(captured, "capture exact W5 proof snapshot");
            snapshot = captured.value;

            AuthoringResult<AuthoringExportReceipt> exported = AuthoringJsonExporter.Export(repository,
                new AuthoringExportRequest
                {
                    documentIds = set.value.documentIds,
                    expectedStoreId = storeId,
                    expectedStoreRevision = repository.ReadStoreInfo().value.storeRevision,
                    destinationRoot = snapshotDirectoryRoot
                });
            Require(exported, "export exact W5 proof snapshot for audit");
            snapshotDirectoryRoot = exported.value.directoryPath;
        }

        string requestPath = Path.Combine(semanticRoot, "snapshot-anchors.json");
        string metadataPath = Path.Combine(semanticRoot, "metadata.json");
        string vectorPath = Path.Combine(semanticRoot, "vectors.bytes");
        WriteSemanticRequest(snapshot, requestPath);
        RunOfflineGteBake(projectRoot, requestPath, metadataPath, vectorPath, Path.Combine(semanticRoot, "cache"));
        ContentHostVocabulary vocabulary = CreateHostVocabulary(snapshot, projectRoot);
        var semantic = new ContentSemanticBuildInput
        {
            metadataUtf8 = File.ReadAllBytes(metadataPath),
            vectorBytes = File.ReadAllBytes(vectorPath),
            tokenizerBytes = File.ReadAllBytes(Path.Combine(projectRoot, "Assets/Resources/AlterOS/Semantics/Models/tokenizer.json")),
            expectedContentHash = snapshot.snapshotHash,
            hostVocabulary = vocabulary
        };
        ContentResult<ContentBakeArtifact> baked = ContentBakeService.BakeFromSnapshot(snapshot, releasePath, semantic);
        Require(baked, "bake pinned W5 proof release");
        ContentResult<RuntimeContentStore> opened = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
        {
            databasePath = releasePath,
            expectedStoreId = baked.value.storeId,
            expectedReleaseId = baked.value.releaseId,
            expectedBuildManifestHash = baked.value.buildManifestHash
        });
        Require(opened, "open staged W5 proof release");

        string report = "W5 isolated proof release\n" +
            "authoringStoreId=" + storeId + "\n" +
            "authoringRevision=" + snapshot.storeRevision.ToString(CultureInfo.InvariantCulture) + "\n" +
            "snapshotHash=" + snapshot.snapshotHash + "\n" +
            "snapshotDocuments=" + snapshot.documents.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
            "snapshotExport=" + snapshotDirectoryRoot + "\n" +
            "releasePath=" + releasePath + "\n" +
            "releaseId=" + baked.value.releaseId + "\n" +
            "buildManifestHash=" + baked.value.buildManifestHash + "\n" +
            "topics=" + baked.value.topics.ToString(CultureInfo.InvariantCulture) + "\n" +
            "flows=" + baked.value.flows.ToString(CultureInfo.InvariantCulture) + "\n" +
            "anchors=" + baked.value.anchors.ToString(CultureInfo.InvariantCulture) + "\n";
        File.WriteAllText(Path.Combine(outputRoot, "W5-proof-release.txt"), report, new UTF8Encoding(false));
        Console.WriteLine(report);
    }

    private static void IntegrateSharedStore(string[] args)
    {
        string baselineRoot = Path.GetFullPath(args[0]);
        string preparedRoot = Path.GetFullPath(args[1]);
        string outputRoot = Path.GetFullPath(args[2]);
        string projectRoot = Path.GetFullPath(args[3]);
        string expectedStoreId = args[4];
        long expectedStoreRevision;
        if (!long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out expectedStoreRevision))
            throw new InvalidOperationException("The expected shared store revision is invalid.");
        string expectedSelectionHash = args[6];
        string expectedSnapshotHash = args[7];
        string authoringBackupPath = Path.GetFullPath(args[8]);
        string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new InvalidOperationException("Choose a new integration output directory under the system temporary folder.");
        if (!File.Exists(authoringBackupPath)) throw new FileNotFoundException("The pre-integration authoring backup is missing.", authoringBackupPath);
        Directory.CreateDirectory(outputRoot);

        AuthoringSnapshot baseline = ReadExportSnapshot(baselineRoot);
        AuthoringSnapshot prepared = ReadExportSnapshot(preparedRoot);
        if (baseline.storeId != expectedStoreId || baseline.storeRevision != expectedStoreRevision || baseline.snapshotHash != expectedSnapshotHash)
            throw new InvalidDataException("The retained baseline export no longer matches the shared-store preflight identity.");
        var baselineById = baseline.documents.ToDictionary(item => item.documentId, StringComparer.Ordinal);
        var preparedById = prepared.documents.ToDictionary(item => item.documentId, StringComparer.Ordinal);
        if (baseline.documents.Count != 439 || prepared.documents.Count != 464 || baseline.storeId == prepared.storeId)
            throw new InvalidDataException("The retained and prepared snapshots do not have the expected bounded source inventory.");
        AssertW5SourceInventory(baselineById, preparedById);

        var changed = prepared.documents.Where(document => !baselineById.ContainsKey(document.documentId) ||
            baselineById[document.documentId].revisionHash != document.revisionHash).ToList();
        AuthoringDocument greeting = baseline.documents.Single(item => item.kind == "legacy_sequence" && item.logicalPath == "greetings.json");
        if (changed.Count != 26 || changed.Count(item => item.documentId == greeting.documentId) != 1)
            throw new InvalidDataException("The prepared source delta must contain exactly one retained greeting edit and twenty-five authored fixtures.");
        foreach (AuthoringDocument document in prepared.documents)
            if (baselineById.ContainsKey(document.documentId) && document.documentId != greeting.documentId &&
                baselineById[document.documentId].revisionHash != document.revisionHash)
                throw new InvalidDataException("A retained authored source changed outside the approved greeting choice: " + document.logicalPath);

        string authoringPath = Path.Combine(projectRoot, "Tools/ContentWorkbench/Workspace/authoring.sqlite3");
        AuthoringResult<AuthoringRepository> opened = AuthoringRepository.OpenExisting(authoringPath);
        Require(opened, "open shared authoring store for the released W5 integration window");
        using (AuthoringRepository repository = opened.value)
        {
            AuthoringResult<AuthoringStoreInfo> info = repository.ReadStoreInfo();
            Require(info, "read shared authoring store before integration");
            if (info.value.storeId != expectedStoreId || info.value.storeRevision != expectedStoreRevision)
                throw new InvalidDataException("Shared store identity or revision changed after the released preflight; no source writes were attempted.");
            AuthoringResult<AuthoringBuildSet> currentSet = repository.ReadBuildSet("default");
            Require(currentSet, "read shared default build set before integration");
            if (currentSet.value.revisionHash != expectedSelectionHash ||
                !SameIds(currentSet.value.documentIds, baseline.documents.Select(item => item.documentId)))
                throw new InvalidDataException("Shared default selection changed after preflight; no source writes were attempted.");
            AuthoringResult<AuthoringSnapshot> currentSnapshot = repository.ReadSnapshot(currentSet.value.documentIds);
            Require(currentSnapshot, "capture exact shared baseline before integration");
            if (currentSnapshot.value.snapshotHash != expectedSnapshotHash)
                throw new InvalidDataException("Shared source snapshot changed after preflight; no source writes were attempted.");
            foreach (AuthoringDocument expected in baseline.documents)
            {
                AuthoringDocument actual = currentSnapshot.value.documents.Single(item => item.documentId == expected.documentId);
                if (actual.revisionHash != expected.revisionHash || actual.kind != expected.kind || actual.logicalPath != expected.logicalPath ||
                    actual.sourceId != expected.sourceId)
                    throw new InvalidDataException("A shared source identity differs from the retained baseline: " + expected.documentId);
            }
            foreach (AuthoringDocument document in changed.Where(item => !baselineById.ContainsKey(item.documentId)))
            {
                AuthoringResult<AuthoringDocument> collision = repository.Read(document.documentId, string.Empty);
                if (collision.IsReady || collision.diagnostics.Count == 0 || collision.diagnostics[0].code != "authoring_document_missing")
                    throw new InvalidDataException("A prepared fixture ID already exists or could not be checked safely: " + document.documentId);
            }

            RewriteW5References(changed, expectedStoreId, preparedById);
            prepared.storeId = expectedStoreId;
            prepared.storeRevision = checked(expectedStoreRevision + changed.Count + 1);
            prepared.documents = prepared.documents.OrderBy(item => item.documentId, StringComparer.Ordinal).ToList();
            prepared.totalBytes = prepared.documents.Sum(item => (long)item.documentBytes.Length);
            prepared.snapshotHash = ComputeSnapshotHash(prepared.storeId, prepared.documents);
            string snapshotError;
            if (!AuthoringRepository.ValidateSnapshot(prepared, out snapshotError))
                throw new InvalidDataException("The prepared shared-store snapshot is invalid: " + snapshotError);

            string semanticRoot = Path.Combine(outputRoot, "gte");
            Directory.CreateDirectory(semanticRoot);
            string requestPath = Path.Combine(semanticRoot, "snapshot-anchors.json");
            string metadataPath = Path.Combine(semanticRoot, "metadata.json");
            string vectorPath = Path.Combine(semanticRoot, "vectors.bytes");
            WriteSemanticRequest(prepared, requestPath);
            RunOfflineGteBake(projectRoot, requestPath, metadataPath, vectorPath, Path.Combine(semanticRoot, "cache"));
            ContentHostVocabulary vocabulary = CreateHostVocabulary(prepared, projectRoot);
            var semantic = new ContentSemanticBuildInput
            {
                metadataUtf8 = File.ReadAllBytes(metadataPath), vectorBytes = File.ReadAllBytes(vectorPath),
                tokenizerBytes = File.ReadAllBytes(Path.Combine(projectRoot, "Assets/Resources/AlterOS/Semantics/Models/tokenizer.json")),
                expectedContentHash = prepared.snapshotHash, hostVocabulary = vocabulary
            };
            string stagedPath = Path.Combine(outputRoot, "w5-shared-content.sqlite3");
            ContentResult<ContentBakeArtifact> baked = ContentBakeService.BakeFromSnapshot(prepared, stagedPath, semantic);
            Require(baked, "bake exact future shared W5 selection before applying CAS writes");
            ContentResult<RuntimeContentStore> openedRelease = RuntimeContentStore.OpenTrustedRelease(new ContentRuntimeConfiguration
            {
                databasePath = stagedPath, expectedStoreId = baked.value.storeId,
                expectedReleaseId = baked.value.releaseId, expectedBuildManifestHash = baked.value.buildManifestHash
            });
            Require(openedRelease, "open exact future shared W5 release before applying CAS writes");

            foreach (AuthoringDocument document in changed.OrderBy(item => item.documentId, StringComparer.Ordinal))
            {
                AuthoringSaveRequest request = new AuthoringSaveRequest
                {
                    documentId = document.documentId, kind = document.kind, logicalPath = document.logicalPath,
                    expectedHash = baselineById.ContainsKey(document.documentId) ? baselineById[document.documentId].revisionHash : string.Empty,
                    documentBytes = document.documentBytes,
                    operationId = "w5-shared-" + Hash(Encoding.UTF8.GetBytes(document.documentId)).Substring(0, 24)
                };
                AuthoringResult<AuthoringDocument> saved = repository.Save(request);
                Require(saved, "CAS-save shared W5 source " + document.documentId);
                if (saved.value.revisionHash != document.revisionHash)
                    throw new InvalidDataException("The shared repository stored different bytes for " + document.documentId);
            }

            List<string> selectedIds = new List<string>(currentSet.value.documentIds);
            selectedIds.AddRange(changed.Where(item => !baselineById.ContainsKey(item.documentId))
                .Select(item => item.documentId).OrderBy(item => item, StringComparer.Ordinal));
            AuthoringResult<AuthoringBuildSet> savedSet = repository.SaveBuildSet(new AuthoringBuildSetRequest
            {
                name = "default", documentIds = selectedIds, expectedHash = expectedSelectionHash,
                operationId = "w5-shared-default-selection"
            });
            Require(savedSet, "CAS-update shared default build set");
            AuthoringResult<AuthoringSnapshot> installedSnapshot = repository.ReadSnapshot(savedSet.value.documentIds);
            Require(installedSnapshot, "capture post-write shared W5 snapshot");
            if (installedSnapshot.value.snapshotHash != prepared.snapshotHash || installedSnapshot.value.documents.Count != prepared.documents.Count)
                throw new InvalidDataException("The committed shared selection does not match the staged W5 release snapshot; the runtime artifact was not installed.");
            foreach (AuthoringDocument document in prepared.documents)
            {
                AuthoringDocument actual = installedSnapshot.value.documents.Single(item => item.documentId == document.documentId);
                if (actual.revisionHash != document.revisionHash || actual.sourceId != document.sourceId)
                    throw new InvalidDataException("The committed shared snapshot differs from the staged source revision: " + document.documentId);
            }
            AuthoringResult<AuthoringStoreInfo> finalInfo = repository.ReadStoreInfo();
            Require(finalInfo, "read final shared authoring identity");
            string exportRoot = Path.Combine(outputRoot, "shared-export");
            Directory.CreateDirectory(exportRoot);
            AuthoringResult<AuthoringExportReceipt> export = AuthoringJsonExporter.Export(repository, new AuthoringExportRequest
            {
                documentIds = savedSet.value.documentIds, expectedStoreId = finalInfo.value.storeId,
                expectedStoreRevision = finalInfo.value.storeRevision, destinationRoot = exportRoot
            });
            Require(export, "export final shared W5 snapshot for recovery");

            string report = "W5 shared integration stage\n" +
                "authoringStoreId=" + finalInfo.value.storeId + "\n" +
                "authoringRevision=" + finalInfo.value.storeRevision.ToString(CultureInfo.InvariantCulture) + "\n" +
                "selectionRevision=" + savedSet.value.revisionHash + "\n" +
                "baselineSnapshotHash=" + expectedSnapshotHash + "\n" +
                "snapshotHash=" + installedSnapshot.value.snapshotHash + "\n" +
                "snapshotDocuments=" + installedSnapshot.value.documents.Count.ToString(CultureInfo.InvariantCulture) + "\n" +
                "snapshotExport=" + export.value.directoryPath + "\n" +
                "authoringBackup=" + authoringBackupPath + "\n" +
                "releasePath=" + stagedPath + "\n" +
                "releaseId=" + baked.value.releaseId + "\n" +
                "buildManifestHash=" + baked.value.buildManifestHash + "\n" +
                "topics=" + baked.value.topics.ToString(CultureInfo.InvariantCulture) + "\n" +
                "flows=" + baked.value.flows.ToString(CultureInfo.InvariantCulture) + "\n" +
                "anchors=" + baked.value.anchors.ToString(CultureInfo.InvariantCulture) + "\n";
            File.WriteAllText(Path.Combine(outputRoot, "W5-shared-integration.txt"), report, new UTF8Encoding(false));
            Console.WriteLine(report);
        }
    }

    private static AuthoringSnapshot ReadExportSnapshot(string exportRoot)
    {
        JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(exportRoot, "manifest.json")));
        var snapshot = new AuthoringSnapshot
        {
            storeId = (string)manifest["storeId"], storeRevision = (long)manifest["storeRevision"],
            snapshotHash = (string)manifest["snapshotHash"]
        };
        JArray rows = manifest["documents"] as JArray;
        if (rows == null) throw new InvalidDataException("An explicit authoring export has no source inventory.");
        foreach (JObject row in rows)
        {
            string relative = (string)row["originalPath"];
            string full = Path.GetFullPath(Path.Combine(exportRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = exportRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An export source path escapes its named snapshot root.");
            byte[] bytes = File.ReadAllBytes(full);
            string hash = Hash(bytes);
            if (hash != (string)row["revisionHash"]) throw new InvalidDataException("An authored snapshot source does not match its recorded immutable hash.");
            snapshot.documents.Add(new AuthoringDocument
            {
                documentId = (string)row["documentId"], kind = (string)row["kind"], sourceId = (string)row["sourceId"],
                logicalPath = (string)row["logicalPath"], revisionHash = hash, documentBytes = bytes
            });
            snapshot.totalBytes += bytes.Length;
        }
        string error;
        if (!AuthoringRepository.ValidateSnapshot(snapshot, out error)) throw new InvalidDataException("An explicit authoring export is invalid: " + error);
        return snapshot;
    }

    private static void AssertW5SourceInventory(IDictionary<string, AuthoringDocument> baseline,
        IDictionary<string, AuthoringDocument> prepared)
    {
        string[] fixtureIds =
        {
            "action.turn_on_tv", "action.locate_tv", "action.fetch_water", "action.grant_water",
            "dialogue.turn_on_tv.opening", "dialogue.locate_tv.bark", "dialogue.fetch_water.prompt", "dialogue.fetch_water.cheer",
            "item.water_glass", "furniture.catalogue", "furniture.seat.basic", "furniture.table.general", "furniture.table.study",
            "furniture.bed.basic", "furniture.water_dispenser", "furniture.tv.basic", "furniture.window.basic", "furniture.carpet.basic",
            "furniture.tv.test_variant", "scenario.turn_on_tv", "scenario.locate_tv", "scenario.fetch_water",
            "topic.turn_on_tv", "topic.locate_tv", "topic.fetch_water"
        };
        string[] actual = prepared.Keys.Where(id => !baseline.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] expected = fixtureIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("The prepared fixture source IDs differ from the exact W5 authored inventory.");
    }

    private static void RewriteW5References(IEnumerable<AuthoringDocument> changed, string targetStoreId,
        IDictionary<string, AuthoringDocument> allDocuments)
    {
        var parsed = changed.ToDictionary(item => item.documentId,
            item => (JObject)JObject.Parse(new UTF8Encoding(false, true).GetString(item.documentBytes)), StringComparer.Ordinal);
        var modified = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JObject> entry in parsed)
            if (RewriteReferenceStores(entry.Value, targetStoreId)) modified.Add(entry.Key);

        var actionRevisions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JObject> entry in parsed.Where(pair => allDocuments[pair.Key].kind == "action_definition"))
        {
            if (modified.Contains(entry.Key))
            {
                AuthoringDocument document = allDocuments[entry.Key];
                document.documentBytes = Utf8(entry.Value);
                document.revisionHash = Hash(document.documentBytes);
            }
            actionRevisions[entry.Key] = allDocuments[entry.Key].revisionHash;
        }

        var scenarioRevisions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JObject> entry in parsed.Where(pair => allDocuments[pair.Key].kind == "scenario_definition"))
        {
            foreach (JObject reference in Descendants(entry.Value).OfType<JObject>().Where(IsPinnedReference))
            {
                string id = (string)reference["id"];
                if (actionRevisions.TryGetValue(id, out string revision))
                { reference["revision"] = revision; reference["expectedHash"] = revision; }
            }
            AuthoringDocument document = allDocuments[entry.Key];
            document.documentBytes = Utf8(entry.Value);
            document.revisionHash = Hash(document.documentBytes);
            scenarioRevisions[entry.Key] = Hash(Encoding.UTF8.GetBytes("scenario\n" + Canonical(entry.Value)));
        }

        foreach (KeyValuePair<string, JObject> entry in parsed.Where(pair => allDocuments[pair.Key].kind == "topic" ||
            allDocuments[pair.Key].kind == "topic_definition"))
        {
            foreach (JObject reference in Descendants(entry.Value).OfType<JObject>().Where(IsPinnedReference))
            {
                string id = (string)reference["id"];
                if (scenarioRevisions.TryGetValue(id, out string revision))
                { reference["store"] = targetStoreId; reference["revision"] = revision; reference["expectedHash"] = revision; }
            }
            AuthoringDocument document = allDocuments[entry.Key];
            document.documentBytes = Utf8(entry.Value);
            document.revisionHash = Hash(document.documentBytes);
        }

        foreach (string id in modified.Where(id => allDocuments[id].kind == "legacy_sequence"))
        {
            allDocuments[id].documentBytes = Utf8(parsed[id]);
            allDocuments[id].revisionHash = Hash(allDocuments[id].documentBytes);
        }
    }

    private static bool RewriteReferenceStores(JToken token, string targetStoreId)
    {
        bool changed = false;
        JObject obj = token as JObject;
        if (obj != null)
        {
            if (IsPinnedReference(obj) && (string)obj["store"] != targetStoreId)
            { obj["store"] = targetStoreId; changed = true; }
            foreach (JProperty property in obj.Properties().ToArray())
                changed |= RewriteReferenceStores(property.Value, targetStoreId);
        }
        else
        {
            JArray array = token as JArray;
            if (array != null) foreach (JToken item in array) changed |= RewriteReferenceStores(item, targetStoreId);
        }
        return changed;
    }

    private static IEnumerable<JToken> Descendants(JToken token)
    {
        if (token == null) return Enumerable.Empty<JToken>();
        JContainer container = token as JContainer;
        return container == null ? new[] { token } : new[] { token }.Concat(container.Descendants());
    }

    private static bool IsPinnedReference(JObject value)
    { return value != null && value["store"] != null && value["id"] != null && value["revision"] != null && value["expectedHash"] != null; }

    private static bool SameIds(IEnumerable<string> left, IEnumerable<string> right)
    { return new HashSet<string>(left ?? Enumerable.Empty<string>(), StringComparer.Ordinal).SetEquals(right ?? Enumerable.Empty<string>()); }

    private static string ComputeSnapshotHash(string storeId, IEnumerable<AuthoringDocument> documents)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            WriteString(writer, storeId);
            foreach (AuthoringDocument document in documents.OrderBy(item => item.documentId, StringComparer.Ordinal))
            {
                WriteString(writer, document.documentId); WriteString(writer, document.kind); WriteString(writer, document.sourceId);
                WriteString(writer, document.logicalPath); WriteString(writer, document.revisionHash);
            }
            writer.Flush();
            return Hash(stream.ToArray());
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    { byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty); writer.Write(bytes.Length); writer.Write(bytes); }

    private static Source EditGreeting(AuthoringRepository repository, IEnumerable<AuthoringDocumentSummary> documents)
    {
        AuthoringDocumentSummary summary = documents.Single(item => item.kind == "legacy_sequence" && item.logicalPath.EndsWith("greetings.json", StringComparison.OrdinalIgnoreCase));
        AuthoringResult<AuthoringDocument> read = repository.Read(summary.documentId, summary.revisionHash);
        Require(read, "read retained greeting sequence");
        JObject root = JObject.Parse(Encoding.UTF8.GetString(read.value.documentBytes));
        JObject favor = (root["nodes"] as JArray).OfType<JObject>().Single(item => (string)item["id"] == "greetings_favor");
        JArray choices = (JArray)favor["choices"];
        choices.Insert(0, new JObject { ["label"] = "Turn on the TV", ["nextId"] = "", ["action"] = "topic_start", ["value"] = "topic.turn_on_tv" });
        return new Source { id = summary.documentId, kind = summary.kind, path = summary.logicalPath,
            operation = "w5-greeting-topic-choice", bytes = Utf8(root) };
    }

    private static List<Source> CreateReusableFixtureSources(string projectRoot, string storeId, AuthoringRepository repository)
    {
        var sources = new List<Source>();
        JObject catalogue = JObject.Parse(File.ReadAllText(Path.Combine(projectRoot, "Assets/StreamingAssets/Furniture/catalogue.json")));
        JArray definitions = (JArray)catalogue["definitions"];
        if (definitions.Count != 8) throw new InvalidDataException("The canonical catalogue must contain all eight starter definitions.");

        foreach (JObject sourceDefinition in definitions)
        {
            string id = (string)sourceDefinition["definitionId"];
            JObject definition = (JObject)sourceDefinition.DeepClone();
            JArray functions = new JArray();
            JObject displayName = Text("text." + id.Replace('.', '_'), HumanName(id), JapaneseName(id));
            definition["displayNameKey"] = (string)displayName["id"];
            if (id == "furniture.water_dispenser")
            {
                JObject point = (JObject)((JArray)definition["points"]).Single(item => (string)item["pointId"] == "drink");
                JArray useIds = (JArray)point["useIds"];
                if (!useIds.Values<string>().Contains("dispenser.collect", StringComparer.Ordinal)) useIds.Add("dispenser.collect");
                definition["presentationBindings"] = new JArray(new JObject
                { ["descriptorId"] = "dispenser.collect_mock", ["pointId"] = "drink", ["useId"] = "dispenser.collect" });
            }
            if (id == "furniture.tv.basic")
            {
                JObject point = (JObject)((JArray)definition["points"]).Single(item => (string)item["pointId"] == "view.target");
                ((JArray)point["useIds"]).Add("tv.power"); ((JArray)point["useIds"]).Add("tv.viewing");
                definition["functionBindings"] = new JArray(new JObject { ["functionId"] = "power.set", ["pointId"] = "view.target", ["useId"] = "tv.power" });
                functions.Add(new JObject { ["functionId"] = "power.set", ["pointId"] = "view.target", ["useId"] = "tv.power", ["contractVersion"] = 1,
                    ["contract"] = new JObject { ["desiredState"] = new JArray("Off", "On") } });
            }
            sources.Add(FurnitureSource(id, displayName, definition, functions));
        }

        JObject basicTv = (JObject)definitions.Single(item => (string)item["definitionId"] == "furniture.tv.basic").DeepClone();
        basicTv["definitionId"] = "furniture.tv.test_variant";
        basicTv["displayNameKey"] = "text.furniture_tv_test_variant";
        JObject variantPoint = (JObject)((JArray)basicTv["points"]).Single(item => (string)item["pointId"] == "view.target");
        variantPoint["useIds"] = new JArray("tv.power", "tv.viewing");
        basicTv["functionBindings"] = new JArray(new JObject { ["functionId"] = "power.set", ["pointId"] = "view.target", ["useId"] = "tv.power" });
        sources.Add(FurnitureSource("furniture.tv.test_variant", Text("text.furniture_tv_test_variant", "Test variant TV", "試作テレビ"), basicTv,
            new JArray(new JObject { ["functionId"] = "power.set", ["pointId"] = "view.target", ["useId"] = "tv.power", ["contractVersion"] = 1,
                ["contract"] = new JObject { ["desiredState"] = new JArray("Off", "On") } })));

        AppendTvSettingsAliases(catalogue);
        JObject settings = (JObject)catalogue.DeepClone();
        settings.Remove("definitions");
        settings["catalogueVersion"] = "1.1.0";
        sources.Add(NewSource("furniture.catalogue", "furniture_catalogue_settings", "Furniture/catalogue-settings.json", settings));

        Source turnDialogue = AddDialogue(sources, "dialogue.turn_on_tv.opening", "Opening", "Sure, I'll turn on the TV.", "うん、テレビをつけるね。");
        AddDialogue(sources, "dialogue.locate_tv.bark", "Here", "Hey, it's here.", "ねえ、ここにあるよ。");
        Source fetchPrompt = AddDialogue(sources, "dialogue.fetch_water.prompt", "Fetch", "I'll get you a drink.", "飲み物を持ってくるね。");
        Source fetchCheer = AddDialogue(sources, "dialogue.fetch_water.cheer", "Cheer", "Here, cheer!", "はい、どうぞ！");
        sources.Add(NewSource("item.water_glass", "item_definition", "Items/item.water_glass.json",
            new JObject { ["schemaVersion"] = 1, ["id"] = "item.water_glass", ["displayName"] = Text("text.water_glass", "Water glass", "水のグラス"), ["stackLimit"] = 99 }));

        AddSequence(sources, "action.turn_on_tv", "turn_on_tv", new JArray(
            Reserve("tv.power", "power.set", "reserve", "move"),
            Move("move", "target", "invoke"),
            Invoke("invoke", "target", "power.set", "On", "complete"), Complete()));
        AddSequence(sources, "action.locate_tv", "locate", new JArray(
            Reserve("tv.viewing", null, "reserve", "move"), Move("move", "target", "bark"),
            Bark("bark", "dialogue.locate_tv.bark", "complete"), Complete()));
        AddSequence(sources, "action.fetch_water", "fetch_water", new JArray(
            Reserve("dispenser.collect", "dispenser.collect_mock", "reserve", "move"), Move("move", "target", "collect"),
            Invoke("collect", "target", "dispenser.collect_mock", null, "return"), MoveNearPlayer("return", "complete"), Complete()));

        Source waterItem = sources.Single(itemSource => itemSource.id == "item.water_glass");
        string itemRevision = Hash(waterItem.bytes);
        JObject grant = new JObject { ["schemaVersion"] = 1, ["id"] = "action.grant_water", ["capabilityId"] = "player.item.grant", ["implementationKind"] = "host_effect",
            ["data"] = new JObject { ["functionId"] = "player.item.grant", ["arguments"] = new JObject { ["itemRef"] = Ref(storeId, waterItem.id, itemRevision), ["quantity"] = 1 } } };
        sources.Add(NewSource("action.grant_water", "action_definition", "Actions/action.grant_water.json", grant));

        Source turnAction = sources.Single(itemSource => itemSource.id == "action.turn_on_tv");
        Source locateAction = sources.Single(itemSource => itemSource.id == "action.locate_tv");
        Source fetchAction = sources.Single(itemSource => itemSource.id == "action.fetch_water");
        string turnFlow = AddScenario(sources, "scenario.turn_on_tv", "topic.turn_on_tv", new JArray(
            DialogueStep("opening", turnDialogue, storeId, "turn_on"), ActionStep("turn_on", turnAction, storeId, "finish"), EndStep("finish", "completed"), EndStep("failed", "failed")), "opening");
        string locateFlow = AddScenario(sources, "scenario.locate_tv", "topic.locate_tv", new JArray(
            ActionStep("locate", locateAction, storeId, "finish"), EndStep("finish", "completed"), EndStep("failed", "failed")), "locate");
        string fetchFlow = AddScenario(sources, "scenario.fetch_water", "topic.fetch_water", new JArray(
            DialogueStep("prompt", fetchPrompt, storeId, "fetch"), ActionStep("fetch", fetchAction, storeId, "cheer"),
            DialogueStep("cheer", fetchCheer, storeId, "grant"), HostEffectStep("grant", "action.grant_water", Hash(sources.Single(s => s.id == "action.grant_water").bytes), storeId, "cheer", "finish", "failed"),
            EndStep("finish", "completed"), EndStep("failed", "failed")), "prompt");

        sources.Add(NewTopic("topic.turn_on_tv", "Turn on the TV", "Can you help me turn on the TV?", "Can you hlep me turn on the tv", "テレビをつけてくれる？", "テレビをつけてくれますか", "scenario.turn_on_tv", storeId, turnFlow));
        sources.Add(NewTopic("topic.locate_tv", "Locate the TV", "Where is the tv?", "Where is the tv?", "テレビはどこ？", "テレビはどこにありますか", "scenario.locate_tv", storeId, locateFlow));
        sources.Add(NewTopic("topic.fetch_water", "Fetch a drink", "Can you fetch me a drink?", "Can you fetch me a drink?", "飲み物を持ってきてくれる？", "飲み物を持ってきてくれますか", "scenario.fetch_water", storeId, fetchFlow));
        return sources;
    }

    private static Source FurnitureSource(string id, JObject displayName, JObject definition, JArray functions)
    { return NewSource(id, "furniture_definition", "Furniture/" + id + ".json", new JObject { ["schemaVersion"] = 1, ["id"] = id, ["displayName"] = displayName, ["definition"] = definition, ["functions"] = functions }); }

    private static void AppendTvSettingsAliases(JObject settings)
    {
        JArray aliases = (JArray)settings["legacyAliases"];
        foreach (JObject alias in new[] {
            new JObject { ["legacyId"] = "testbox.tv.basic.fixture_a", ["definitionId"] = "furniture.tv.basic" },
            new JObject { ["legacyId"] = "testbox.tv.basic.fixture_a.view_target", ["definitionId"] = "furniture.tv.basic", ["pointId"] = "view.target" },
            new JObject { ["legacyId"] = "testbox.tv.test_variant.fixture_b", ["definitionId"] = "furniture.tv.test_variant" },
            new JObject { ["legacyId"] = "testbox.tv.test_variant.fixture_b.view_target", ["definitionId"] = "furniture.tv.test_variant", ["pointId"] = "view.target" }
        }) aliases.Add(alias);
    }

    private static Source AddDialogue(List<Source> sources, string id, string beatId, string english, string japanese)
    {
        JObject dialogue = new JObject { ["schemaVersion"] = 1, ["id"] = id, ["beats"] = new JArray(new JObject
        { ["beatId"] = beatId, ["text"] = Text("text." + id.Replace('.', '_'), english, japanese), ["expression"] = "neutral" }) };
        Source source = NewSource(id, "dialogue_definition", "Dialogues/" + id + ".json", dialogue);
        sources.Add(source);
        return source;
    }

    private static void AddSequence(List<Source> sources, string id, string capability, JArray steps)
    { sources.Add(NewSource(id, "action_definition", "Actions/" + id + ".json", new JObject { ["schemaVersion"] = 1, ["id"] = id, ["capabilityId"] = "scenario.activity_sequence", ["implementationKind"] = "activity_sequence", ["data"] = new JObject { ["steps"] = steps } })); }

    private static JObject Reserve(string useId, string functionId, string id, string next)
    {
        var args = new JArray(new JObject { ["name"] = "useId", ["value"] = Literal("text", useId) });
        if (!string.IsNullOrEmpty(functionId)) args.Add(new JObject { ["name"] = "functionId", ["value"] = Literal("enum", functionId) });
        return Step(id, "reserve_nearest", args, new JArray(new JObject { ["resultName"] = "targetToken", ["bindingId"] = "target" }), next, "", "", "after");
    }
    private static JObject Move(string id, string target, string next)
    { return Step(id, "move_to", new JArray(new JObject { ["name"] = "target", ["value"] = Binding(target) }), new JArray(new JObject { ["resultName"] = "arrived", ["bindingId"] = "arrived" }), next, "", "", "after"); }
    private static JObject Invoke(string id, string target, string functionId, string desiredState, string next)
    {
        var args = new JArray(new JObject { ["name"] = "target", ["value"] = Binding(target) }, new JObject { ["name"] = "functionId", ["value"] = Literal("enum", functionId) });
        if (!string.IsNullOrEmpty(desiredState)) args.Add(new JObject { ["name"] = "desiredState", ["value"] = Literal("enum", desiredState) });
        return Step(id, "invoke_furniture", args, new JArray(), next, "", "", "after");
    }
    private static JObject Bark(string id, string dialogueId, string next)
    { return Step(id, "bark", new JArray(new JObject { ["name"] = "dialogueId", ["value"] = Literal("text", dialogueId) }), new JArray(), next, "", "", "none"); }
    private static JObject MoveNearPlayer(string id, string next)
    { return Step(id, "move_near_player", new JArray(), new JArray(new JObject { ["resultName"] = "arrived", ["bindingId"] = "returned" }), next, "", "", "after"); }
    private static JObject Complete() { return Step("complete", "complete", new JArray(), new JArray(), "", "", "", "none"); }
    private static JObject Step(string id, string primitive, JArray args, JArray bindings, string success, string failure, string cancel, string checkpoint)
    { return new JObject { ["stepId"] = id, ["primitiveId"] = primitive, ["arguments"] = args, ["resultBindings"] = bindings, ["successNextStepId"] = success, ["failureNextStepId"] = failure, ["cancelNextStepId"] = cancel, ["checkpointPolicy"] = checkpoint }; }
    private static JObject Literal(string type, string value) { return new JObject { ["type"] = type, ["literal"] = value }; }
    private static JObject Binding(string id) { return new JObject { ["bindingId"] = id }; }

    private static string AddScenario(List<Source> sources, string id, string topicId, JArray steps, string entry)
    {
        JObject flow = new JObject { ["scenarioId"] = id, ["topicId"] = topicId, ["entryStepId"] = entry, ["steps"] = steps };
        Source source = NewSource(id, "scenario_definition", "Scenarios/" + id + ".json", flow);
        sources.Add(source);
        return ScenarioRevision(flow);
    }
    private static JObject DialogueStep(string id, Source dialogue, string storeId, string next)
    { return new JObject { ["id"] = id, ["kind"] = "dialogue", ["dialogueRef"] = Ref(storeId, dialogue.id, Hash(dialogue.bytes)), ["nextStepId"] = next }; }
    private static JObject ActionStep(string id, Source action, string storeId, string next)
    { return new JObject { ["id"] = id, ["kind"] = "action", ["actionRef"] = Ref(storeId, action.id, Hash(action.bytes)), ["successNextStepId"] = next, ["failureNextStepId"] = "failed" }; }
    private static JObject HostEffectStep(string id, string actionId, string revision, string storeId, string prerequisite, string next, string failure)
    { return new JObject { ["id"] = id, ["kind"] = "host_effect", ["actionRef"] = Ref(storeId, actionId, revision), ["data"] = new JObject { ["schemaVersion"] = 1, ["requiresCompletedStepId"] = prerequisite }, ["successNextStepId"] = next, ["failureNextStepId"] = failure }; }
    private static JObject EndStep(string id, string outcome) { return new JObject { ["id"] = id, ["kind"] = "end", ["outcome"] = outcome }; }

    private static Source NewTopic(string id, string label, string anchorEnA, string anchorEnB, string anchorJaA, string anchorJaB,
        string scenarioId, string storeId, string scenarioRevision)
    {
        JObject topic = new JObject { ["schemaVersion"] = 1, ["topicId"] = id, ["menuLabel"] = label, ["prompt"] = label,
            ["anchors"] = new JArray(anchorEnA, anchorEnB, anchorJaA, anchorJaB), ["defaultReplayPolicy"] = "repeatable", ["cooldownSeconds"] = 0,
            ["scenarios"] = new JArray(new JObject { ["scenarioId"] = "entry." + id.Replace('.', '_'), ["priority"] = 1, ["baseWeight"] = 1,
                ["condition"] = JValue.CreateNull(), ["channels"] = new JArray("direct"), ["modifiers"] = new JArray(), ["consumptionNodeId"] = "",
                ["scenarioRef"] = Ref(storeId, scenarioId, scenarioRevision) }) };
        return NewSource(id, "topic", "Topics/" + id + ".json", new JObject { ["status"] = "draft", ["document"] = topic });
    }

    private static JObject Text(string id, string en, string ja)
    { return new JObject { ["id"] = id, ["translations"] = new JArray(new JObject { ["language"] = "en", ["text"] = en }, new JObject { ["language"] = "ja", ["text"] = ja }) }; }
    private static JObject Ref(string store, string id, string revision)
    { return new JObject { ["store"] = store, ["id"] = id, ["revision"] = revision, ["expectedHash"] = revision }; }
    private static byte[] RefBytes(string store, string id, string revision) { return Utf8(Ref(store, id, revision)); }
    private static Source NewSource(string id, string kind, string path, JObject json)
    { return new Source { id = id, kind = kind, path = path, operation = "w5-save-" + id.Replace('.', '-'), bytes = Utf8(json) }; }

    private static void Save(AuthoringRepository repository, Source source)
    {
        AuthoringResult<AuthoringDocumentPage> page = repository.List(source.kind, "", 100);
        Require(page, "locate existing source " + source.id);
        AuthoringDocumentSummary found = page.value.documents.FirstOrDefault(item => item.documentId == source.id);
        while (found == null && !string.IsNullOrEmpty(page.value.nextCursor))
        {
            page = repository.List(source.kind, page.value.nextCursor, 100);
            Require(page, "page existing source " + source.id);
            found = page.value.documents.FirstOrDefault(item => item.documentId == source.id);
        }
        AuthoringResult<AuthoringDocument> saved = repository.Save(new AuthoringSaveRequest
        {
            documentId = source.id, kind = source.kind, logicalPath = source.path,
            expectedHash = found == null ? "" : found.revisionHash,
            operationId = source.operation, documentBytes = source.bytes
        });
        Require(saved, "save authored fixture " + source.id);
    }

    private static void WriteSemanticRequest(AuthoringSnapshot snapshot, string path)
    {
        var records = new List<JObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (AuthoringDocument document in snapshot.documents.Where(item => item.kind == "generic_dialogue"))
        {
            JObject root = JObject.Parse(Encoding.UTF8.GetString(document.documentBytes));
            string route = (string)root["routing"]?["routeId"];
            JArray examples = root["routing"]?["examples"] as JArray;
            if (string.IsNullOrWhiteSpace(route) || examples == null) throw new InvalidDataException("Selected Generic document lacks a route/examples: " + document.logicalPath);
            string ownerRevision = Hash(Encoding.UTF8.GetBytes("gte-route-owner\0" + route));
            foreach (string text in examples.Values<string>()) AddAnchor(records, seen, "canonical", route, ownerRevision, "gte_route", text);
        }
        foreach (AuthoringDocument document in snapshot.documents.Where(item => item.kind == "topic" || item.kind == "topic_definition"))
        {
            JObject root = JObject.Parse(Encoding.UTF8.GetString(document.documentBytes));
            JObject topic = root["document"] as JObject ?? root;
            string id = (string)topic["topicId"];
            JArray topicAnchors = topic["anchors"] as JArray ?? new JArray();
            string revision = Hash(Encoding.UTF8.GetBytes("topic\n" + Canonical(topic)));
            for (int index = 0; index < topicAnchors.Count; index++)
                AddAnchor(records, seen, "topic", id, revision, "anchor_" + index.ToString("D4", CultureInfo.InvariantCulture), (string)topicAnchors[index]);
        }
        records = records.OrderBy(item => (string)item["ownerKind"], StringComparer.Ordinal).ThenBy(item => (string)item["ownerId"], StringComparer.Ordinal)
            .ThenBy(item => (string)item["ownerRevision"], StringComparer.Ordinal).ThenBy(item => (string)item["role"], StringComparer.Ordinal)
            .ThenBy(item => (string)item["text"], StringComparer.Ordinal).ToList();
        JObject input = new JObject { ["schemaVersion"] = 1, ["snapshotHash"] = snapshot.snapshotHash, ["anchors"] = new JArray(records) };
        File.WriteAllBytes(path, Utf8(input));
    }
    private static void AddAnchor(List<JObject> records, HashSet<string> seen, string kind, string id, string revision, string role, string text)
    {
        text = (text ?? "").Trim(); if (text.Length == 0) return;
        string key = kind + "\0" + id + "\0" + revision + "\0" + role + "\0" + text;
        if (seen.Add(key)) records.Add(new JObject { ["ownerKind"] = kind, ["ownerId"] = id, ["ownerRevision"] = revision, ["role"] = role, ["text"] = text });
    }
    private static void RunOfflineGteBake(string projectRoot, string request, string metadata, string vectors, string cache)
    {
        string python = Path.Combine(projectRoot, "Tools/SemanticLab/.venv/Scripts/python.exe");
        string script = Path.Combine(projectRoot, "Tools/SemanticLab/bake_gte_anchors.py");
        if (!File.Exists(python) || !File.Exists(script)) throw new FileNotFoundException("Pinned offline GTE baking environment is unavailable.", python);
        var start = new ProcessStartInfo { FileName = python, Arguments = Q(script) + " --snapshot-input " + Q(request) + " --output " + Q(vectors) + " --metadata " + Q(metadata) + " --cache-root " + Q(cache),
            WorkingDirectory = Path.GetDirectoryName(script), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.EnvironmentVariables["HF_HUB_OFFLINE"] = "1"; start.EnvironmentVariables["TRANSFORMERS_OFFLINE"] = "1";
        using (Process process = Process.Start(start))
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            string stdout = stdoutTask.Result, stderr = stderrTask.Result;
            if (process.ExitCode != 0) throw new InvalidDataException("Offline GTE bake failed: " + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
            Console.WriteLine(stdout.Trim());
        }
    }

    private static ContentHostVocabulary CreateHostVocabulary(AuthoringSnapshot snapshot, string projectRoot)
    {
        var generic = snapshot.documents.Where(item => item.kind == "generic_dialogue").OrderBy(item => item.logicalPath, StringComparer.Ordinal).ToList();
        string[] routes = generic.Select(item => (string)JObject.Parse(Encoding.UTF8.GetString(item.documentBytes))["routing"]?["routeId"])
            .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        var expressions = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(projectRoot, "Assets/Altera/Presets"), "*.asset", SearchOption.AllDirectories))
        {
            Match match = Regex.Match(File.ReadAllText(path), @"(?m)^\s*expressionName:\s*(\S+)\s*$");
            if (match.Success) expressions.Add(match.Groups[1].Value);
        }
        if (expressions.Count == 0) throw new InvalidDataException("No host expression presets were found.");
        ActivitySequenceCatalog catalog = ActivitySequenceCatalogFactory.CreateDefault();
        string[] capabilities = new[] { "movement.come_here", "movement.follow_player", "needs.drink_water", "scenario.activity_sequence" }
            .Concat(catalog.Primitives.All.Select(item => item.requiredCapabilityId))
            .Concat(ContentHostEffectFunctionRegistry.RegisteredCapabilityIds).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
        return new ContentHostVocabulary(capabilities, expressions, new string[0], new string[0], routes, catalog.CreateBuilder());
    }

    private static string ScenarioRevision(JObject scenario)
    { return Hash(Encoding.UTF8.GetBytes("scenario\n" + Canonical(scenario))); }
    private static string Canonical(JToken token)
    {
        JObject obj = token as JObject;
        if (obj != null) return "{" + string.Join(",", obj.Properties().OrderBy(item => item.Name, StringComparer.Ordinal).Select(item => Quote(item.Name) + ":" + Canonical(item.Value))) + "}";
        JArray arr = token as JArray;
        if (arr != null) return "[" + string.Join(",", arr.Select(Canonical)) + "]";
        return token == null ? "null" : token.ToString(Formatting.None);
    }
    private static string HumanName(string id) { return string.Join(" ", id.Split('.').Skip(1).Select(item => char.ToUpperInvariant(item[0]) + item.Substring(1))); }
    private static string JapaneseName(string id)
    { return id == "furniture.seat.basic" ? "椅子" : id == "furniture.table.general" ? "テーブル" : id == "furniture.table.study" ? "勉強机" : id == "furniture.bed.basic" ? "ベッド" : id == "furniture.water_dispenser" ? "ウォーターサーバー" : id == "furniture.tv.basic" ? "テレビ" : id == "furniture.window.basic" ? "窓" : "カーペット"; }
    private static byte[] Utf8(JToken value) { return new UTF8Encoding(false).GetBytes(value.ToString(Formatting.None)); }
    private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return string.Concat(sha.ComputeHash(bytes).Select(item => item.ToString("x2"))); }
    private static string Quote(string value) { return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    private static string Q(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    private static void Require<T>(AuthoringResult<T> result, string operation)
    { if (result == null || !result.IsReady) throw new InvalidDataException(operation + ": " + (result == null ? "no result" : result.status + " " + string.Join("; ", result.diagnostics.Select(item => item.code + ": " + item.message)))); }
    private static void Require<T>(ContentResult<T> result, string operation)
    { if (result == null || !result.IsReady) throw new InvalidDataException(operation + ": " + (result == null ? "no result" : result.status + " " + string.Join("; ", result.diagnostics.Select(item => item.code + " at " + item.path + ": " + item.message)))); }
}
