$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityVersionLine = Select-String -Path (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion:' | Select-Object -First 1
if ($null -eq $unityVersionLine) { throw 'Unity version was not found in ProjectVersion.txt.' }
$unityVersion = ($unityVersionLine.Line -split ':', 2)[1].Trim()
$unityData = Join-Path $env:ProgramFiles ("Unity\Hub\Editor\$unityVersion\Editor\Data")
$mono = Join-Path $unityData 'MonoBleedingEdge\bin\mono-sgen.exe'
$mcs = Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\mcs.exe'
$unityEngine = Join-Path $unityData 'Managed\UnityEngine'
$scriptAssemblies = Join-Path $projectRoot 'Library/ScriptAssemblies'
$newtonsoft = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$required = @($mono, $mcs, (Join-Path $scriptAssemblies 'Assembly-CSharp.dll'), (Join-Path $scriptAssemblies 'AlterOS.Runtime.dll'))
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Source-only compile dependency is missing: $path" }
}

$references = @(
    (Get-ChildItem $scriptAssemblies -Filter '*.dll' -File | ForEach-Object FullName)
    (Get-ChildItem $unityEngine -Filter '*.dll' -File |
        Where-Object { [string]::IsNullOrWhiteSpace($newtonsoft) -or $_.Name -ne 'Newtonsoft.Json.dll' } |
        ForEach-Object FullName)
    (Join-Path $unityData 'MonoBleedingEdge\lib\mono\4.5\Facades\netstandard.dll')
    $newtonsoft
)
$referenceArguments = @($references | Sort-Object -Unique | ForEach-Object { '-r:' + $_ })
$referenceArguments += '-r:System.Runtime.Serialization.dll'
$sources = @(
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Persistence') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Content') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Stats') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Dialogue') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Topics') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Scenarios') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Conversation') -Filter '*.cs' -File | ForEach-Object FullName)
    (Join-Path $projectRoot 'Assets/Scripts/Interaction/ConversationSessionStore.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Interaction/GeneratedDialogueArchive.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Interaction/RuntimeMigrationStartup.cs')
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Memory') -Filter '*.cs' -File | ForEach-Object FullName)
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Activities') -Filter '*.cs' -File | ForEach-Object FullName)
    (Join-Path $projectRoot 'Assets/AlterOS/Furniture/HomeFurnitureLayout.cs')
    (Join-Path $projectRoot 'Assets/AlterOS/Furniture/HomeFurnitureRepository.cs')
    (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurnitureCatalogue.cs')
    (Join-Path $projectRoot 'Assets/AlterOS/Furniture/FurnitureArrangementResolver.cs')
    (Join-Path $projectRoot 'Assets/AlterOS/Activities/HydrationCheckpointSink.cs')
    (Get-ChildItem (Join-Path $projectRoot 'Assets/AlterOS/Recording') -Filter '*.cs' -File | ForEach-Object FullName)
    (Join-Path $projectRoot 'Assets/Scripts/Progression/AlteraProgressionStore.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Progression/ProgressionTickSystem.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Progression/DialogueEffectApplier.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Progression/ActivityHydrationProgressionBridge.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Progression/StatMoodResolver.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Progression/CompanionStatSnapshotBuilder.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/DialogueManager.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/DialogueData.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/DialogueRouter.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/DirectConversationBridge.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/DirectConversationBridge.ActivityScenarios.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/UI/WorldDialogueChoicePresenter.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/UI/WorldDialogueChoiceButton.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/UI/DialogueCameraDirector.cs')
    (Join-Path $projectRoot 'Assets/Scripts/Dialogue/TopicOptionCatalog.cs')
    (Join-Path $projectRoot 'Assets/Altera/AlteraAmbientBubble.cs')
    (Join-Path $projectRoot 'Assets/Altera/AlteraIdleTrigger.cs')
    (Join-Path $projectRoot 'Assets/Altera/AlteraProximityAmbientTrigger.cs')
)
$outputRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('aos-compile-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$output = Join-Path $outputRoot 'ProgressionIntegrationCheck.dll'
$responseFile = Join-Path $outputRoot 'compile.rsp'
$compilerArguments = @('-langversion:latest', '-target:library', ('-out:' + $output)) + $referenceArguments + $sources
$quotedArguments = $compilerArguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }
[System.IO.File]::WriteAllLines($responseFile, $quotedArguments, [System.Text.UTF8Encoding]::new($false))
& $mono $mcs ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) {
    Write-Output "FAIL: source-only compile exited $LASTEXITCODE. Output retained at $outputRoot"
    exit $LASTEXITCODE
}

$routerSource = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'Assets/Scripts/Dialogue/DialogueRouter.cs'))
$managerSource = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'Assets/Scripts/Dialogue/DialogueManager.cs'))
$publicCinematicIsUntrusted = [regex]::IsMatch($routerSource,
    'public void PlayCinematic\(DialogueSequence sequence\)\s*\{\s*TryPlayCinematic\(sequence,\s*allowGameEffects:\s*false\);\s*\}')
$playFromJsonStart = $routerSource.IndexOf('public void PlayFromJson', [System.StringComparison]::Ordinal)
$preloadStart = $routerSource.IndexOf('public void Preload', $playFromJsonStart, [System.StringComparison]::Ordinal)
$playFromJsonBody = if ($playFromJsonStart -ge 0 -and $preloadStart -gt $playFromJsonStart) {
    $routerSource.Substring($playFromJsonStart, $preloadStart - $playFromJsonStart)
} else { '' }
$genericPlayStart = $routerSource.IndexOf('public void Play(string relativePath)', [System.StringComparison]::Ordinal)
$publicCinematicStart = $routerSource.IndexOf('public void PlayCinematic', $genericPlayStart, [System.StringComparison]::Ordinal)
$genericPlayBody = if ($genericPlayStart -ge 0 -and $publicCinematicStart -gt $genericPlayStart) {
    $routerSource.Substring($genericPlayStart, $publicCinematicStart - $genericPlayStart)
} else { '' }
$publicJsonStartsAreUntrusted = $playFromJsonBody.Contains('RouteAsCinematic(json);') -and
    -not $playFromJsonBody.Contains('StartAuthoredSequence(') -and
    $genericPlayBody.Contains('RouteAsCinematic(json);') -and
    -not $genericPlayBody.Contains('StartAuthoredSequence(')
$trustedRouteStart = $routerSource.IndexOf('public bool TryPlayAuthoredRoute', [System.StringComparison]::Ordinal)
$trustedRouteEnd = $routerSource.IndexOf('public void PlayAmbientPool', $trustedRouteStart, [System.StringComparison]::Ordinal)
$trustedRouteBody = if ($trustedRouteStart -ge 0 -and $trustedRouteEnd -gt $trustedRouteStart) {
    $routerSource.Substring($trustedRouteStart, $trustedRouteEnd - $trustedRouteStart)
} else { '' }
$validationPosition = $trustedRouteBody.IndexOf('TryConvertCanonicalSequence(selectedJson', [System.StringComparison]::Ordinal)
$trustedStartPosition = $trustedRouteBody.IndexOf('TryPlayCinematic(selectedSequence, allowGameEffects: true)', [System.StringComparison]::Ordinal)
$authoredStartCallCount = [regex]::Matches($routerSource, 'dialogueManager\.StartAuthoredSequence\(').Count
$dynamicRouteStart = $routerSource.IndexOf('private void RouteAsCinematic', [System.StringComparison]::Ordinal)
$dynamicRouteEnd = $routerSource.IndexOf('private void EnsureAuthoredRouteCatalog', $dynamicRouteStart, [System.StringComparison]::Ordinal)
$dynamicRouteBody = if ($dynamicRouteStart -ge 0 -and $dynamicRouteEnd -gt $dynamicRouteStart) {
    $routerSource.Substring($dynamicRouteStart, $dynamicRouteEnd - $dynamicRouteStart)
} else { '' }
$dynamicRouteIsUntrusted = $dynamicRouteBody.Contains('dialogueManager.StartSequence(sequence);') -and
    -not $dynamicRouteBody.Contains('StartAuthoredSequence(')
$localStartStart = $managerSource.IndexOf('private bool TryPlayLocalSequenceChoice', [System.StringComparison]::Ordinal)
$localStartEnd = $managerSource.IndexOf('private void OpenTopicInput', $localStartStart, [System.StringComparison]::Ordinal)
$localStartBody = if ($localStartStart -ge 0 -and $localStartEnd -gt $localStartStart) {
    $managerSource.Substring($localStartStart, $localStartEnd - $localStartStart)
} else { '' }
$localStartIsUntrusted = $localStartBody.Contains('StartSequence(sequence);') -and
    -not $localStartBody.Contains('StartAuthoredSequence(')
$testMenuStartIsUntrusted = [regex]::IsMatch($managerSource,
    'Treat as a standard Cinematic Sequence[\s\S]{0,240}StartSequence\(json\);')
$legacyJsonTrustOverloadRemoved = -not [regex]::IsMatch($managerSource, 'StartAuthoredSequence\(string json')
if (-not $publicCinematicIsUntrusted -or $validationPosition -lt 0 -or
    $trustedStartPosition -le $validationPosition -or $authoredStartCallCount -ne 1 -or
    -not $dynamicRouteIsUntrusted -or -not $publicJsonStartsAreUntrusted -or
    -not $localStartIsUntrusted -or -not $testMenuStartIsUntrusted -or -not $legacyJsonTrustOverloadRemoved) {
    Write-Output 'FAIL: dialogue authored-effect trust boundary did not match the expected validated-route-only policy.'
    Write-Output "Details: publicCinematicUntrusted=$publicCinematicIsUntrusted, publicJsonStartsUntrusted=$publicJsonStartsAreUntrusted, validatedRoute=$($validationPosition -ge 0 -and $trustedStartPosition -gt $validationPosition), authoredCalls=$authoredStartCallCount, dynamicRouteUntrusted=$dynamicRouteIsUntrusted, localStartUntrusted=$localStartIsUntrusted, testMenuUntrusted=$testMenuStartIsUntrusted, rawJsonTrustRemoved=$legacyJsonTrustOverloadRemoved"
    exit 7
}
Write-Output 'PASS: only catalogue-selected, canonically validated file-backed routes can use the authored-effects start path; generic file playback, provider JSON, caller-supplied sequences, and local/test sequence starts remain untrusted.'

$probeSource = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;

public static class ProgressionShapeProbe
{
    public static int Main(string[] args)
    {
        if (args == null || args.Length != 6) return 2;
        Assembly assembly = Assembly.LoadFrom(args[0]);
        Assembly runtimeAssembly = Assembly.LoadFrom(args[5]);
        Type store = assembly.GetType("AlterOS.Progression.AlteraProgressionStore", true);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        MethodInfo complete = store.GetMethod("HasCompleteSchema4Shape", flags);
        MethodInfo readVersion = store.GetMethod("TryReadProgressionSchemaVersion", flags);
        if (complete == null || readVersion == null) return 3;

        bool valid = (bool)complete.Invoke(null, new object[] { args[1] });
        bool incomplete = (bool)complete.Invoke(null, new object[] { args[2] });
        object[] malformedHeader = { args[3], -1 };
        bool malformedAccepted = (bool)readVersion.Invoke(null, malformedHeader);
        object[] missingHeader = { args[4], -1 };
        bool legacyVersionAccepted = (bool)readVersion.Invoke(null, missingHeader);
        bool legacyVersionZero = (int)missingHeader[1] == 0;
        if (!valid || incomplete || malformedAccepted || !legacyVersionAccepted || !legacyVersionZero) return 4;
        Console.WriteLine("PASS: complete schema-4 accepted; incomplete snapshot and malformed header rejected; absent legacy version remains schema 0.");

        Type deltaType = assembly.GetType("AlterOS.Dialogue.StatDelta", true);
        Type effectType = assembly.GetType("AlterOS.Dialogue.DialogueEffect", true);
        MethodInfo validate = store.GetMethod("ValidateDialogueMutation", flags);
        if (validate == null) return 5;
        bool smallChange = Validate(validate, CreateList(deltaType, Delta(deltaType, "altera", "affinity", 0.5f, "add")), null);
        bool credit = Validate(validate, CreateList(deltaType, Delta(deltaType, "player", "credits", 25f, "add")), null);
        bool largeChange = Validate(validate, CreateList(deltaType, Delta(deltaType, "altera", "affinity", 1000000f, "add")), null);
        bool reward = Validate(validate, null, CreateList(effectType, Reward(effectType)));
        if (!smallChange || credit || largeChange || reward) return 6;
        Console.WriteLine("PASS: bounded dialogue policy accepts a small authored change; rejects credits, oversized deltas, and reward effects.");

        Type stateType = store.GetProperty("State").PropertyType;
        Type alteraType = stateType.GetField("altera").FieldType;
        object state = Activator.CreateInstance(stateType);
        object altera = Activator.CreateInstance(alteraType);
        alteraType.GetField("energy").SetValue(altera, 95f);
        stateType.GetField("altera").SetValue(state, altera);
        object storeObject = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(store);
        store.GetField("<State>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(storeObject, state);
        MethodInfo applyStat = store.GetMethod("ApplyStatChange", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo getStat = store.GetMethod("GetStatInternal", BindingFlags.Instance | BindingFlags.NonPublic);
        Type runtimeDeltaType = applyStat.GetParameters()[0].ParameterType;
        applyStat.Invoke(storeObject, new[] { Delta(runtimeDeltaType, "altera", "energy", 10f, "add") });
        float clamped = (float)getStat.Invoke(storeObject, new object[] { "altera", "energy", float.NaN });
        MethodInfo makeHistory = store.GetMethod("CreateStatHistory", BindingFlags.NonPublic | BindingFlags.Static);
        Type historyType = makeHistory.ReturnType;
        object clampedHistory = makeHistory.Invoke(null, new object[] { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            0, "alterra", Guid.NewGuid().ToString("D"), "alterra.energy", 95f, clamped, false });
        object unknownHistory = makeHistory.Invoke(null, new object[] { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            0, "alterra", Guid.NewGuid().ToString("D"), "alterra.hydration", float.NaN, 0f, false });
        if (clamped != 100f || (float)historyType.GetField("appliedDelta").GetValue(clampedHistory) != 5f ||
            (bool)historyType.GetField("beforeKnown").GetValue(unknownHistory) ||
            historyType.GetField("appliedDelta").GetValue(unknownHistory) != null) return 7;
        Console.WriteLine("PASS: history records the actual clamped delta and preserves an unknown pre-change value.");

        Type contentReader = runtimeAssembly.GetType("AlterOS.Content.IContentReleaseReader", true);
        Type layout = runtimeAssembly.GetType("AlterOS.Furniture.HomeFurnitureLayoutDocument", true);
        Type seedProvider = typeof(Func<,>).MakeGenericType(typeof(string), layout);
        MethodInfo hostEffectFactory = store.GetMethod("CreateScenarioHostEffectPort", new[] { contentReader });
        MethodInfo powerFactory = store.GetMethod("CreateFurniturePowerStore", new[] { seedProvider });
        if (hostEffectFactory == null || powerFactory == null) return 8;
        object notReadyStore = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(store);
        Type runtimeStore = runtimeAssembly.GetType("AlterOS.Content.RuntimeContentStore", true);
        object contentReaderStub = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(runtimeStore);
        if (hostEffectFactory.Invoke(notReadyStore, new[] { contentReaderStub }) != null ||
            powerFactory.Invoke(notReadyStore, new object[] { null }) != null) return 9;

        Type repositoryType = runtimeAssembly.GetType("AlterOS.Persistence.SqliteGameRepository", true);
        string syntheticRoot = Path.Combine(Path.GetDirectoryName(args[0]), "synthetic-progression-bridge");
        Directory.CreateDirectory(syntheticRoot);
        object repository = Activator.CreateInstance(repositoryType, new object[] { Path.Combine(syntheticRoot, "game.sqlite3") });
        object bridgeStore = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(store);
        store.GetField("repository", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridgeStore, repository);
        store.GetField("<IsInitialized>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridgeStore, true);
        store.GetField("<State>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridgeStore, state);
        store.GetField("writesBlocked", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridgeStore, true);
        if (hostEffectFactory.Invoke(bridgeStore, new[] { contentReaderStub }) != null ||
            powerFactory.Invoke(bridgeStore, new object[] { null }) != null) return 10;
        store.GetField("writesBlocked", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridgeStore, false);
        object hostEffect = hostEffectFactory.Invoke(bridgeStore, new[] { contentReaderStub });
        object furniturePower = powerFactory.Invoke(bridgeStore, new object[] { null });
        if (hostEffectFactory.Invoke(bridgeStore, new object[] { null }) != null || hostEffect == null || furniturePower == null) return 11;
        FieldInfo hostGame = hostEffect.GetType().GetField("game", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo powerGame = furniturePower.GetType().GetField("game", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo powerFurniture = furniturePower.GetType().GetField("furniture", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo furnitureGame = powerFurniture == null ? null : powerFurniture.FieldType.GetField("repository", BindingFlags.Instance | BindingFlags.NonPublic);
        if (hostGame == null || powerGame == null || furnitureGame == null || !object.ReferenceEquals(hostGame.GetValue(hostEffect), repository) ||
            !object.ReferenceEquals(powerGame.GetValue(furniturePower), repository) ||
            !object.ReferenceEquals(furnitureGame.GetValue(powerFurniture.GetValue(furniturePower)), repository)) return 12;
        Console.WriteLine("PASS: typed progression adapters fail closed before readiness/write availability and share one synthetic-root repository.");

        return 0;
    }

    private static bool Validate(MethodInfo method, object changes, object effects)
    {
        object[] arguments = { changes, effects, string.Empty };
        return (bool)method.Invoke(null, arguments);
    }

    private static IList CreateList(Type elementType, object item)
    {
        IList result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        result.Add(item);
        return result;
    }

    private static object Delta(Type type, string target, string stat, float amount, string op)
    {
        object value = Activator.CreateInstance(type);
        type.GetField("target").SetValue(value, target);
        type.GetField("stat").SetValue(value, stat);
        type.GetField("amount").SetValue(value, amount);
        type.GetField("op").SetValue(value, op);
        return value;
    }

    private static object Reward(Type type)
    {
        object value = Activator.CreateInstance(type);
        type.GetField("type").SetValue(value, "reward");
        type.GetField("key").SetValue(value, "credits");
        type.GetField("amount").SetValue(value, 25f);
        return value;
    }
}
'@
$probeSourcePath = Join-Path $outputRoot 'ProgressionShapeProbe.cs'
$probeExecutable = Join-Path $outputRoot 'ProgressionShapeProbe.exe'
[System.IO.File]::WriteAllText($probeSourcePath, $probeSource, [System.Text.UTF8Encoding]::new($false))
& $mono $mcs -langversion:latest ('-out:' + $probeExecutable) $probeSourcePath
if ($LASTEXITCODE -ne 0) {
    Write-Output "FAIL: synthetic schema probe compile exited $LASTEXITCODE. Output retained at $outputRoot"
    exit $LASTEXITCODE
}

$validSnapshot = '{"schemaVersion":4,"lastUpdatedUtc":"","altera":{"bondLevel":1,"bondXp":0,"affinity":0,"trust":0,"comfort":50,"energy":70,"hydration":0,"hydrationKnown":false,"engagement":50,"stimulation":45,"attention":50,"reassurance":50,"mood":"calm","availability":"present"},"player":{"credits":77,"completedTasks":0,"focusSessionsCompleted":0,"streak":0,"productivityXp":0,"reliability":50,"focusEnergy":70,"stress":20,"rest":70,"currentMode":"free"},"environment":{"comfort":50,"cleanliness":50,"lighting":50,"noise":20,"temperature":50,"coziness":50,"personalization":0,"focusAtmosphere":50,"timeOfDay":"unknown","weather":"clear","roomMood":"neutral"},"flags":[{"key":"fixture","value":"kept"}],"unlocks":[],"customStats":[],"tasks":[]}'
$incompleteSnapshot = '{"schemaVersion":4,"player":{"credits":5}}'
$malformedHeader = '{"schemaVersion":"four","player":{"credits":5}}'
$missingHeader = '{"player":{"credits":5}}'
$oldMonoPath = $env:MONO_PATH
$env:MONO_PATH = @($outputRoot, $scriptAssemblies, $unityEngine, (Join-Path $unityData 'Managed')) -join ';'
try {
    & $mono $probeExecutable $output $validSnapshot $incompleteSnapshot $malformedHeader $missingHeader (Join-Path $scriptAssemblies 'AlterOS.Runtime.dll')
    if ($LASTEXITCODE -ne 0) {
        Write-Output "FAIL: synthetic schema-shape probe exited $LASTEXITCODE. Output retained at $outputRoot"
        exit $LASTEXITCODE
    }
}
finally {
    $env:MONO_PATH = $oldMonoPath
}

Write-Output "PASS: Unity $unityVersion bundled compiler built persistence and progression/dialogue integration sources. Output: $output"
