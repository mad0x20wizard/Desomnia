namespace System.Reflection
{
    public static class ReflectionExtensions
    {
        public static PropertyInfo[] GetAllProperties(this Type? type, BindingFlags bindingFlags)
        {
            List<PropertyInfo> properties = [];

            while (type != null)
            {
                properties.AddRange(type.GetProperties(bindingFlags | BindingFlags.DeclaredOnly));

                type = type.BaseType;
            }

            return [.. properties];
        }

        public static FieldInfo[] GetAllFields(this Type? type, BindingFlags bindingFlags)
        {
            List<FieldInfo> fields = [];

            while (type != null)
            {
                fields.AddRange(type.GetFields(bindingFlags | BindingFlags.DeclaredOnly));

                type = type.BaseType;
            }

            return [.. fields];
        }

        public static MethodInfo[] GetAllMethods(this Type? type, BindingFlags bindingFlags)
        {
            List<MethodInfo> methods = [];

            while (type != null)
            {
                methods.AddRange(type.GetMethods(bindingFlags | BindingFlags.DeclaredOnly));

                type = type.BaseType;
            }

            return [.. methods];
        }
    }
}