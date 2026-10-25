using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace AlterOS.ContentWorkbench
{
    internal static class WorkbenchCommandHost
    {
        private const int MaximumRequestBytes = 1024 * 1024;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            object response;
            try
            {
                if (args.Length != 0)
                    response = WorkbenchCommands.Invalid("", "invalid_arguments", "The command host accepts one JSON request on standard input.");
                else
                {
                    string json = ReadBoundedRequest(Console.OpenStandardInput());
                    response = WorkbenchCommands.Handle(JsonWire.Deserialize(json), WorkbenchEnvironment.FromExecutable());
                }
            }
            catch (RequestTooLargeException)
            {
                response = WorkbenchCommands.Invalid("", "request_too_large", "Request body exceeds the 1 MiB limit.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Content Workbench host failure: " + exception.GetType().Name);
                response = WorkbenchCommands.Invalid("", "request_invalid", "The request could not be processed.");
            }

            try
            {
                Console.Out.WriteLine(JsonWire.Serialize(response));
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Content Workbench response failure: " + exception.GetType().Name);
                return 1;
            }
        }

        private static string ReadBoundedRequest(Stream input)
        {
            using (var bytes = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (bytes.Length + read > MaximumRequestBytes)
                        throw new RequestTooLargeException();
                    bytes.Write(buffer, 0, read);
                }
                return new UTF8Encoding(false, true).GetString(bytes.ToArray());
            }
        }

        private sealed class RequestTooLargeException : Exception { }
    }

    internal static class JsonWire
    {
        private const int MaximumJsonCharacters = 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumJsonCharacters, RecursionLimit = 64 };
        }

        public static object Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            return CreateSerializer().DeserializeObject(json);
        }

        public static object Deserialize(byte[] bytes)
        {
            return Deserialize(Utf8.GetString(bytes));
        }

        public static string Serialize(object value)
        {
            return CreateSerializer().Serialize(value);
        }

        public static byte[] SerializeBytes(object value)
        {
            return Utf8.GetBytes(Serialize(value));
        }

        public static IDictionary<string, object> AsObject(object value)
        {
            return value as IDictionary<string, object>;
        }

        public static IEnumerable AsArray(object value)
        {
            return value as IEnumerable;
        }

        public static string String(IDictionary<string, object> source, string key, string fallback = "")
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value is string ? (string)value : fallback;
        }

        public static bool TryBoolean(IDictionary<string, object> source, string key, out bool value)
        {
            object raw;
            if (source != null && source.TryGetValue(key, out raw) && raw is bool)
            {
                value = (bool)raw;
                return true;
            }
            value = false;
            return false;
        }

        public static bool TryDouble(IDictionary<string, object> source, string key, out double value)
        {
            object raw;
            if (source != null && source.TryGetValue(key, out raw) && IsNumber(raw))
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            value = 0d;
            return false;
        }

        public static bool TryInt(IDictionary<string, object> source, string key, out int value)
        {
            object raw;
            if (source != null && source.TryGetValue(key, out raw) && IsNumber(raw))
            {
                double number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (number >= int.MinValue && number <= int.MaxValue && Math.Truncate(number) == number)
                {
                    value = Convert.ToInt32(number, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            value = 0;
            return false;
        }

        public static bool TryLong(IDictionary<string, object> source, string key, out long value)
        {
            object raw;
            if (source != null && source.TryGetValue(key, out raw) && IsNumber(raw))
            {
                decimal number = Convert.ToDecimal(raw, CultureInfo.InvariantCulture);
                if (number >= long.MinValue && number <= long.MaxValue && decimal.Truncate(number) == number)
                {
                    value = Convert.ToInt64(number, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            value = 0;
            return false;
        }

        public static bool IsNumber(object value)
        {
            return value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint ||
                value is long || value is ulong || value is float || value is double || value is decimal;
        }

        public static object ConvertTo(object value, Type targetType)
        {
            if (targetType == typeof(object)) return value;
            if (value == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null) return null;
                return Activator.CreateInstance(targetType);
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null) targetType = nullableType;
            if (targetType == typeof(string))
            {
                if (!(value is string)) throw new InvalidDataException("Expected a JSON string.");
                return value;
            }
            if (targetType == typeof(bool))
            {
                if (!(value is bool)) throw new InvalidDataException("Expected a JSON boolean.");
                return value;
            }
            if (targetType.IsEnum)
            {
                if (!(value is string)) throw new InvalidDataException("Expected a JSON enum string.");
                return Enum.Parse(targetType, (string)value, false);
            }
            if (targetType.IsPrimitive || targetType == typeof(decimal))
            {
                if (!IsNumber(value)) throw new InvalidDataException("Expected a JSON number.");
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            if (targetType.IsArray)
            {
                IEnumerable values = AsArray(value);
                if (values == null || value is string) throw new InvalidDataException("Expected a JSON array.");
                var items = new List<object>();
                foreach (object item in values) items.Add(ConvertTo(item, targetType.GetElementType()));
                Array result = Array.CreateInstance(targetType.GetElementType(), items.Count);
                for (int index = 0; index < items.Count; index++) result.SetValue(items[index], index);
                return result;
            }
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                IEnumerable values = AsArray(value);
                if (values == null || value is string) throw new InvalidDataException("Expected a JSON array.");
                Type itemType = targetType.GetGenericArguments()[0];
                IList result = (IList)Activator.CreateInstance(targetType);
                foreach (object item in values) result.Add(ConvertTo(item, itemType));
                return result;
            }
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                IDictionary<string, object> values = AsObject(value);
                if (values == null || targetType.GetGenericArguments()[0] != typeof(string))
                    throw new InvalidDataException("Expected a JSON object.");
                Type itemType = targetType.GetGenericArguments()[1];
                IDictionary result = (IDictionary)Activator.CreateInstance(targetType);
                foreach (KeyValuePair<string, object> pair in values)
                    result.Add(pair.Key, ConvertTo(pair.Value, itemType));
                return result;
            }

            IDictionary<string, object> fields = AsObject(value);
            if (fields == null) throw new InvalidDataException("Expected a JSON object for " + targetType.Name + ".");
            object instance = Activator.CreateInstance(targetType);
            foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.IsInitOnly || field.IsLiteral) continue;
                object fieldValue;
                if (!fields.TryGetValue(field.Name, out fieldValue)) continue;
                field.SetValue(instance, ConvertTo(fieldValue, field.FieldType));
            }
            return instance;
        }
    }

    internal sealed class WorkbenchEnvironment
    {
        private WorkbenchEnvironment(string projectRoot)
        {
            ProjectRoot = Path.GetFullPath(projectRoot);
            WorkbenchRoot = Path.Combine(ProjectRoot, "Tools", "ContentWorkbench");
            AuthoringWorkspaceRoot = Path.Combine(WorkbenchRoot, "Workspace");
            AuthoringDatabasePath = Path.Combine(AuthoringWorkspaceRoot, "authoring.sqlite3");
            AuthoringImportRoot = Path.Combine(ProjectRoot, "Assets", "StreamingAssets", "Dialogue");
            AuthoringExportsRoot = Path.Combine(ProjectRoot, "Content", "AuthoringExports");
            HostContextsPath = Path.Combine(ProjectRoot, "Tools", "ContentWorkbench", "Fixtures", "sit-together", "host-contexts.json");
            FurnitureCataloguePath = Path.Combine(ProjectRoot, "Assets", "StreamingAssets", "Furniture", "catalogue.json");
        }

        public string ProjectRoot { get; private set; }
        public string WorkbenchRoot { get; private set; }
        public string AuthoringWorkspaceRoot { get; private set; }
        public string AuthoringDatabasePath { get; private set; }
        public string AuthoringImportRoot { get; private set; }
        public string AuthoringExportsRoot { get; private set; }
        public string HostContextsPath { get; private set; }
        public string FurnitureCataloguePath { get; private set; }

        public static WorkbenchEnvironment FromExecutable()
        {
            string current = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            while (!string.IsNullOrEmpty(current))
            {
                string marker = Path.Combine(current, "Assets", "AlterOS", "Topics", "TopicDefinition.cs");
                string workbench = Path.Combine(current, "Tools", "ContentWorkbench");
                if (File.Exists(marker) && Directory.Exists(workbench))
                    return new WorkbenchEnvironment(current);
                DirectoryInfo parent = Directory.GetParent(current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                current = parent == null ? null : parent.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate the AlterOS project root from the command host location.");
        }
    }
}
