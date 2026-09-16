// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Vendored from dotnet/runtime v10.0.8:
//   src/libraries/Microsoft.Extensions.Configuration.Binder/src/ConfigurationBinder.cs
//
// Local changes (marked with "DESOMNIA"):
//  1. Value conversion failures throw ConfigurationValueException, which is rethrown from
//     collection/dictionary/array/set binding instead of being silently swallowed. A typo in
//     a single attribute therefore aborts binding instead of dropping the whole element.
//  2. A value on a known property or collection item that cannot be converted always throws,
//     not only when BinderOptions.ErrorOnUnknownConfiguration is set. Unknown keys stay
//     tolerated ("open" configuration format for modules/plugins).
//  3. An existing-but-empty element ("<element></element>") binds to a default instance of
//     complex types instead of null (replaces the provider's "__empty" attribute hack).
//  4. An element's text content (= the section value) constructs complex types that declare
//     a public constructor with a single string parameter (replaces the provider's synthetic
//     "text" attribute and the private "Text" carrier properties on the config classes).
//     Types whose ONLY constructor takes the string make the text mandatory (a missing text
//     fails loudly). Text content that no constructor consumes stays tolerated, just like
//     unknown attributes — it may address another module's or plugin's view of the element.
//  5. A C# `required` member is enforced: a required property the configuration does not set,
//     and that no constructor or initializer default filled either, aborts the binding
//     (reflection construction bypasses the compiler's required-member check silently).
//  6. Enum values accept "|"-separated flags and dashed member names, TimeSpan values accept
//     friendly formats like "90s", "5min", "7 days" and ISO 8601 durations (replaces the
//     provider's AddEnumAttribute registrations and blanket attribute rewriting).
//
// The public methods are plain static methods (not extension methods) to avoid ambiguity
// with the stock Microsoft.Extensions.Configuration.ConfigurationBinder extensions.

using Microsoft.Extensions.Configuration;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MadWizard.Desomnia.Configuration.Binding
{
    /// <summary>
    /// Static helper class that allows binding strongly typed objects to configuration values.
    /// </summary>
    public static class StrictConfigurationBinder
    {
        private const BindingFlags DeclaredOnlyLookup = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const string DynamicCodeWarningMessage = "Binding strongly typed objects to configuration values requires generating dynamic code at runtime, for example instantiating generic types.";
        private const string TrimmingWarningMessage = "In case the type is non-primitive, the trimmer cannot statically analyze the object's type so its members may be trimmed.";
        private const string InstanceGetTypeTrimmingWarningMessage = "Cannot statically analyze the type of instance so its members may be trimmed";
        private const string PropertyTrimmingWarningMessage = "Cannot statically analyze property.PropertyType so its members may be trimmed.";


        /// <summary>
        /// Attempts to bind the configuration instance to a new instance of type T.
        /// If this configuration section has a value, that will be used.
        /// Otherwise binding by matching property names against configuration keys recursively.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static T? Get<T>(IConfiguration configuration)
            => Get<T>(configuration, null);

        /// <summary>
        /// Attempts to bind the configuration instance to a new instance of type T.
        /// If this configuration section has a value, that will be used.
        /// Otherwise binding by matching property names against configuration keys recursively.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static T? Get<T>(IConfiguration configuration, Action<BinderOptions>? configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            object? result = Get(configuration, typeof(T), configureOptions);
            if (result == null)
            {
                return default(T);
            }
            return (T)result;
        }

        /// <summary>
        /// Attempts to bind the configuration instance to a new instance of the specified type.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static object? Get(IConfiguration configuration, Type type)
            => Get(configuration, type, null);

        /// <summary>
        /// Attempts to bind the configuration instance to a new instance of the specified type.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        public static object? Get(
            IConfiguration configuration,
            Type type,
            Action<BinderOptions>? configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(type);

            var options = new BinderOptions();
            configureOptions?.Invoke(options);
            var bindingPoint = new BindingPoint();
            BindInstance(type, bindingPoint, config: configuration, options: options, isParentCollection: false);
            return bindingPoint.Value;
        }

        /// <summary>
        /// Attempts to bind the given object instance to the configuration section specified by the key by matching property names against configuration keys recursively.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(InstanceGetTypeTrimmingWarningMessage)]
        public static void Bind(IConfiguration configuration, string key, object? instance)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            Bind(configuration.GetSection(key), instance);
        }

        /// <summary>
        /// Attempts to bind the given object instance to configuration values by matching property names against configuration keys recursively.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(InstanceGetTypeTrimmingWarningMessage)]
        public static void Bind(IConfiguration configuration, object? instance)
            => Bind(configuration, instance, null);

        /// <summary>
        /// Attempts to bind the given object instance to configuration values by matching property names against configuration keys recursively.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(InstanceGetTypeTrimmingWarningMessage)]
        public static void Bind(IConfiguration configuration, object? instance, Action<BinderOptions>? configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            if (instance != null)
            {
                var options = new BinderOptions();
                configureOptions?.Invoke(options);
                var bindingPoint = new BindingPoint(instance, isReadOnly: true);
                BindInstance(instance.GetType(), bindingPoint, configuration, options, false);
            }
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(PropertyTrimmingWarningMessage)]
        private static void BindProperties(object instance, IConfiguration configuration, BinderOptions options, ParameterInfo[]? constructorParameters)
        {
            List<PropertyInfo> modelProperties = GetAllProperties(instance.GetType());

            if (options.ErrorOnUnknownConfiguration)
            {
                HashSet<string> propertyNames = new(modelProperties.Select(mp => mp.Name),
                    StringComparer.OrdinalIgnoreCase);

                List<string>? missingPropertyNames = null;
                foreach (IConfigurationSection cs in configuration.GetChildren())
                {
                    if (!propertyNames.Contains(cs.Key))
                    {
                        (missingPropertyNames ??= new()).Add($"'{cs.Key}'");
                    }
                }

                if (missingPropertyNames != null)
                {
                    throw new InvalidOperationException(SR.Format(SR.Error_MissingConfig,
                        nameof(options.ErrorOnUnknownConfiguration), nameof(BinderOptions), instance.GetType(),
                        string.Join(", ", missingPropertyNames)));
                }
            }

            foreach (PropertyInfo property in modelProperties)
            {
                // DESOMNIA: match constructor parameters case-insensitively (upstream compares
                // ordinally, which never matches camelCase parameters to PascalCase properties)
                if (constructorParameters is null || !constructorParameters.Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    BindProperty(property, instance, configuration, options);
                }
                else
                {
                    ResetPropertyValue(property, instance, options);
                }

                EnsureRequiredProperty(property, instance, configuration);
            }
        }

        // DESOMNIA: a C# `required` member is only enforced by the compiler at object-initializer
        // sites - reflection construction bypasses it silently. The binder makes the declaration
        // mean what it says for configuration types: a required property the configuration does
        // not set, and that no constructor or initializer default filled either, aborts the
        // startup instead of surfacing later as a null where none was ever expected.
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(PropertyTrimmingWarningMessage)]
        private static void EnsureRequiredProperty(PropertyInfo property, object instance, IConfiguration configuration)
        {
            if (property.GetCustomAttribute<RequiredMemberAttribute>() is null)
                return;

            if (property.GetMethod is not MethodInfo getter || getter.GetParameters().Length > 0)
                return;

            if (configuration.GetSection(GetPropertyName(property)).Exists())
                return;

            if (getter.Invoke(instance, null) is not null)
                return; // a constructor or initializer default satisfies the requirement

            string subject = configuration is IConfigurationSection section && section.Path.Length > 0
                ? $"'{section.Path}'" : $"a {instance.GetType().Name}";

            throw new ConfigurationValueException(
                $"{subject} requires the '{GetPropertyName(property)}' attribute, but the configuration does not set it.");
        }

        /// <summary>
        /// Reset the property value to the value from the property getter. This is useful for properties that have a getter or setters that perform some logic changing the object state.
        /// </summary>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(PropertyTrimmingWarningMessage)]
        private static void ResetPropertyValue(PropertyInfo property, object instance, BinderOptions options)
        {
            // We don't support set only, non public, or indexer properties
            if (property.GetMethod is null ||
                property.SetMethod is null ||
                (!options.BindNonPublicProperties && (!property.GetMethod.IsPublic || !property.SetMethod.IsPublic)) ||
                property.GetMethod.GetParameters().Length > 0)
            {
                return;
            }

            property.SetValue(instance, property.GetValue(instance));
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(PropertyTrimmingWarningMessage)]
        private static void BindProperty(PropertyInfo property, object instance, IConfiguration config, BinderOptions options)
        {
            // We don't support set only, non public, or indexer properties
            if (property.GetMethod == null ||
                (!options.BindNonPublicProperties && !property.GetMethod.IsPublic) ||
                property.GetMethod.GetParameters().Length > 0)
            {
                return;
            }

            var propertyBindingPoint = new BindingPoint(
                initialValueProvider: () => property.GetValue(instance),
                isReadOnly: property.SetMethod is null || (!property.SetMethod.IsPublic && !options.BindNonPublicProperties));

            BindInstance(
                property.PropertyType,
                propertyBindingPoint,
                config.GetSection(GetPropertyName(property)),
                options,
                false);

            // For property binding, there are some cases when HasNewValue is not set in BindingPoint while a non-null Value inside that object can be retrieved from the property getter.
            // As example, when binding a property which not having a configuration entry matching this property and the getter can initialize the Value.
            // It is important to call the property setter as the setters can have a logic adjusting the Value.
            // Otherwise, if the HasNewValue set to true, it means that the property setter should be called anyway as encountering a new value.
            if (!propertyBindingPoint.IsReadOnly && (propertyBindingPoint.Value is not null || propertyBindingPoint.HasNewValue))
            {
                property.SetValue(instance, propertyBindingPoint.Value);
            }
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        private static void BindInstance(
            Type type,
            BindingPoint bindingPoint,
            IConfiguration config,
            BinderOptions options,
            bool isParentCollection)
        {
            // if binding IConfigurationSection, break early
            if (type == typeof(IConfigurationSection))
            {
                bindingPoint.TrySetValue(config);
                return;
            }

            if (config is null)
            {
                return;
            }

            IConfigurationSection? section;
            string? configValue;
            bool isConfigurationExist;

            if (config is ConfigurationSection configSection)
            {
                section = configSection;
                isConfigurationExist = configSection.TryGetValue(key: null, out configValue);
            }
            else
            {
                section = config as IConfigurationSection;
                configValue = section?.Value;
                isConfigurationExist = configValue != null;
            }

            if (isConfigurationExist && TryConvertValue(type, configValue, section?.Path, out object? convertedValue, out Exception? error))
            {
                if (error != null)
                {
                    throw error;
                }

                if (type == typeof(byte[]) && bindingPoint.Value is byte[] byteArray && byteArray.Length > 0)
                {
                    if (convertedValue is byte[] convertedByteArray && convertedByteArray.Length > 0)
                    {
                        Array a = Array.CreateInstance(type.GetElementType()!, byteArray.Length + convertedByteArray.Length);
                        Array.Copy(byteArray, a, byteArray.Length);
                        Array.Copy(convertedByteArray, 0, a, byteArray.Length, convertedByteArray.Length);
                        bindingPoint.TrySetValue(a);
                    }
                    return;
                }

                // Leaf nodes are always reinitialized
                bindingPoint.TrySetValue(convertedValue);
                return;
            }

            if (config.GetChildren().Any())
            {
                // for arrays and read-only list-like interfaces, we concatenate on to what is already there, if we can
                if (type.IsArray || IsImmutableArrayCompatibleInterface(type))
                {
                    if (!bindingPoint.IsReadOnly)
                    {
                        bindingPoint.SetValue(BindArray(type, (IEnumerable?)bindingPoint.Value, config, options));
                    }

                    // for getter-only collection properties that we can't add to, nothing more we can do
                    return;
                }

                if (TypeIsASetInterface(type))
                {
                    if (!bindingPoint.IsReadOnly || bindingPoint.Value is not null)
                    {
                        object? newValue = BindSet(type, (IEnumerable?)bindingPoint.Value, config, options);
                        if (!bindingPoint.IsReadOnly && newValue != null)
                        {
                            bindingPoint.SetValue(newValue);
                        }
                    }

                    return;
                }

                if (TypeIsADictionaryInterface(type))
                {
                    if (!bindingPoint.IsReadOnly || bindingPoint.Value is not null)
                    {
                        object? newValue = BindDictionaryInterface(bindingPoint.Value, type, config, options);
                        if (!bindingPoint.IsReadOnly && newValue != null)
                        {
                            bindingPoint.SetValue(newValue);
                        }
                    }

                    return;
                }

                ParameterInfo[]? constructorParameters = null;

                // If we don't have an instance, try to create one
                if (bindingPoint.Value is null)
                {
                    // if the binding point doesn't let us set a new instance, there's nothing more we can do
                    if (bindingPoint.IsReadOnly)
                    {
                        return;
                    }

                    Type? interfaceGenericType = type.IsInterface && type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : null;

                    if (interfaceGenericType is not null &&
                        (interfaceGenericType == typeof(ICollection<>) || interfaceGenericType == typeof(IList<>)))
                    {
                        // For ICollection<T> and IList<T> we bind them to mutable List<T> type.
                        Type genericType = typeof(List<>).MakeGenericType(type.GenericTypeArguments);
                        bindingPoint.SetValue(Activator.CreateInstance(genericType));
                    }
                    // DESOMNIA: an element's text content (= the section value) constructs types
                    // declaring a single-string constructor; attributes/children bind afterwards.
                    // Without such a constructor the text is ignored, like unknown attributes are —
                    // it may address another module's or plugin's view of the same element.
                    else if (!string.IsNullOrWhiteSpace(configValue) && FindTextConstructor(type) is ConstructorInfo textConstructor)
                    {
                        bindingPoint.SetValue(InvokeTextConstructor(textConstructor, configValue.Trim(), section));
                    }
                    else
                    {
                        bindingPoint.SetValue(CreateInstance(type, config, options, out constructorParameters));
                    }
                }

                Debug.Assert(bindingPoint.Value is not null);

                // At this point we know that we have a non-null bindingPoint.Value, we just have to populate the items
                // using the IDictionary<> or ICollection<> interfaces, or properties using reflection.
                Type? dictionaryInterface = FindOpenGenericInterface(typeof(IDictionary<,>), type);

                if (dictionaryInterface != null)
                {
                    BindDictionary(bindingPoint.Value, dictionaryInterface, config, options);
                }
                else
                {
                    Type? collectionInterface = FindOpenGenericInterface(typeof(ICollection<>), type);
                    if (collectionInterface != null)
                    {
                        BindCollection(bindingPoint.Value, collectionInterface, config, options);
                    }
                    else
                    {
                        BindProperties(bindingPoint.Value, config, options, constructorParameters);
                    }
                }
            }
            else
            {
                // Reaching this point indicates that the configuration section is a leaf node with a string value.
                // Typically, configValue will be an empty string if the value in the configuration is empty or null.
                // While configValue could be any other string, we already know it cannot be converted to the required type, as TryConvertValue has already failed.

                if (!string.IsNullOrEmpty(configValue))
                {
                    // DESOMNIA: an element with only text content ("<rule>^regex$</rule>") constructs
                    // complex types declaring a single-string constructor. Otherwise the value is
                    // ignored like upstream does, and like unknown attributes are — it may address
                    // another module's or plugin's view of the same element (open format).
                    if (!bindingPoint.IsReadOnly && bindingPoint.Value is null &&
                        FindTextConstructor(type) is ConstructorInfo textConstructor)
                    {
                        bindingPoint.SetValue(InvokeTextConstructor(textConstructor, configValue.Trim(), section));
                    }
                    else if (options.ErrorOnUnknownConfiguration)
                    {
                        Debug.Assert(section is not null);
                        throw new InvalidOperationException(SR.Format(SR.Error_FailedBinding, configValue, section?.Path, type));
                    }
                }
                else if (!bindingPoint.IsReadOnly && bindingPoint.Value is null)
                {
                    if (isParentCollection)
                    {
                        // Try to create the default instance of the type
                        bindingPoint.TrySetValue(CreateInstance(type, config, options, out _));
                    }
                    else if (isConfigurationExist)
                    {
                        if (type.IsArray || IsIEnumerableInterface(type))
                        {
                            // When having configuration value set to empty string, we create an empty array
                            bindingPoint.TrySetValue(configValue is null ? null : Array.CreateInstance(type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0], 0));
                        }
                        // DESOMNIA: an existing-but-empty element ("<element></element>") binds complex
                        // types to a default instance instead of null, so the mere presence of the
                        // element is observable (replaces the "__empty" attribute hack).
                        else if (configValue is not null && !type.IsInterface && !type.IsAbstract)
                        {
                            bindingPoint.TrySetValue(CreateInstance(type, config, options, out _));
                        }
                        else
                        {
                            bindingPoint.TrySetValue(bindingPoint.Value); // force setting null value
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Create an instance of the specified type.
        /// </summary>
        /// <exception cref="InvalidOperationException">If the type cannot be created.</exception>
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(
            "In case type is a Nullable<T>, cannot statically analyze what the underlying type is so its members may be trimmed.")]
        private static object CreateInstance(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors |
                                        DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            Type type,
            IConfiguration config,
            BinderOptions options,
            out ParameterInfo[]? constructorParameters)
        {
            constructorParameters = null;

            if (type.IsInterface || type.IsAbstract)
            {
                throw new InvalidOperationException(SR.Format(SR.Error_CannotActivateAbstractOrInterface, type));
            }

            ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            bool hasParameterlessConstructor =
                type.IsValueType || constructors.Any(ctor => ctor.GetParameters().Length == 0);

            if (!type.IsValueType && constructors.Length == 0)
            {
                throw new InvalidOperationException(SR.Format(SR.Error_MissingPublicInstanceConstructor, type));
            }

            if (constructors.Length > 1 && !hasParameterlessConstructor)
            {
                throw new InvalidOperationException(SR.Format(SR.Error_MultipleParameterizedConstructors, type));
            }

            if (constructors.Length == 1 && !hasParameterlessConstructor)
            {
                ConstructorInfo constructor = constructors[0];
                ParameterInfo[] parameters = constructor.GetParameters();

                if (!CanBindToTheseConstructorParameters(parameters, out string nameOfInvalidParameter))
                {
                    throw new InvalidOperationException(SR.Format(SR.Error_CannotBindToConstructorParameter, type, nameOfInvalidParameter));
                }


                List<PropertyInfo> properties = GetAllProperties(type);

                if (!DoAllParametersHaveEquivalentProperties(parameters, properties, out string nameOfInvalidParameters))
                {
                    throw new InvalidOperationException(SR.Format(SR.Error_ConstructorParametersDoNotMatchProperties, type, nameOfInvalidParameters));
                }

                object?[] parameterValues = new object?[parameters.Length];

                for (int index = 0; index < parameters.Length; index++)
                {
                    parameterValues[index] = BindParameter(parameters[index], type, config, options);
                }

                constructorParameters = parameters;

                return constructor.Invoke(parameterValues);
            }

            object? instance;
            try
            {
                instance = Activator.CreateInstance(Nullable.GetUnderlyingType(type) ?? type);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(SR.Format(SR.Error_FailedToActivate, type), ex);
            }

            return instance ?? throw new InvalidOperationException(SR.Format(SR.Error_FailedToActivate, type));
        }

        private static bool DoAllParametersHaveEquivalentProperties(ParameterInfo[] parameters,
            List<PropertyInfo> properties, out string missing)
        {
            HashSet<string> propertyNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo prop in properties)
            {
                propertyNames.Add(prop.Name);
            }

            List<string> missingParameters = new();

            foreach (ParameterInfo parameter in parameters)
            {
                string name = parameter.Name!;
                if (!propertyNames.Contains(name))
                {
                    missingParameters.Add(name);
                }
            }

            missing = string.Join(",", missingParameters);

            return missing.Length == 0;
        }

        private static bool CanBindToTheseConstructorParameters(ParameterInfo[] constructorParameters, out string nameOfInvalidParameter)
        {
            nameOfInvalidParameter = string.Empty;
            foreach (ParameterInfo p in constructorParameters)
            {
                if (p.IsOut || p.IsIn || p.ParameterType.IsByRef)
                {
                    nameOfInvalidParameter = p.Name!; // never null as we're not passed return value parameters
                    return false;
                }
            }

            return true;
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode("Cannot statically analyze what the element type is of the value objects in the dictionary so its members may be trimmed.")]
        private static object? BindDictionaryInterface(
            object? source,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
            Type dictionaryType,
            IConfiguration config, BinderOptions options)
        {
            // IDictionary<K,V> is guaranteed to have exactly two parameters
            Type keyType = dictionaryType.GenericTypeArguments[0];
            Type valueType = dictionaryType.GenericTypeArguments[1];
            bool keyTypeIsEnum = keyType.IsEnum;
            bool keyTypeIsInteger =
                keyType == typeof(sbyte) ||
                keyType == typeof(byte) ||
                keyType == typeof(short) ||
                keyType == typeof(ushort) ||
                keyType == typeof(int) ||
                keyType == typeof(uint) ||
                keyType == typeof(long) ||
                keyType == typeof(ulong);

            if (keyType != typeof(string) && !keyTypeIsEnum && !keyTypeIsInteger)
            {
                // We only support string, enum and integer (except nint-IntPtr and nuint-UIntPtr) keys
                return null;
            }

            // addMethod can only be null if dictionaryType is IReadOnlyDictionary<TKey, TValue> rather than IDictionary<TKey, TValue>.
            MethodInfo? addMethod = dictionaryType.GetMethod("Add", DeclaredOnlyLookup);
            if (addMethod is null || source is null)
            {
                dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                object? dictionary = Activator.CreateInstance(dictionaryType);
                addMethod = dictionaryType.GetMethod("Add", DeclaredOnlyLookup);

                var orig = source as IEnumerable;
                if (orig is not null)
                {
                    Type kvpType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
                    PropertyInfo keyMethod = kvpType.GetProperty("Key", DeclaredOnlyLookup)!;
                    PropertyInfo valueMethod = kvpType.GetProperty("Value", DeclaredOnlyLookup)!;
                    object?[] arguments = new object?[2];

                    foreach (object? item in orig)
                    {
                        object? k = keyMethod.GetMethod!.Invoke(item, null);
                        object? v = valueMethod.GetMethod!.Invoke(item, null);
                        arguments[0] = k;
                        arguments[1] = v;
                        addMethod!.Invoke(dictionary, arguments);
                    }
                }

                source = dictionary;
            }

            Debug.Assert(source is not null);
            Debug.Assert(addMethod is not null);

            BindDictionary(source, dictionaryType, config, options);

            return source;
        }

        // Binds and potentially overwrites a dictionary object.
        // This differs from BindDictionaryInterface because this method doesn't clone
        // the dictionary; it sets and/or overwrites values directly.
        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode("Cannot statically analyze what the element type is of the value objects in the dictionary so its members may be trimmed.")]
        private static void BindDictionary(
            object dictionary,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
            Type dictionaryType,
            IConfiguration config, BinderOptions options)
        {
            Debug.Assert(dictionaryType.IsGenericType &&
                         (dictionaryType.GetGenericTypeDefinition() == typeof(IDictionary<,>) || dictionaryType.GetGenericTypeDefinition() == typeof(Dictionary<,>)));

            Type keyType = dictionaryType.GenericTypeArguments[0];
            Type valueType = dictionaryType.GenericTypeArguments[1];
            bool keyTypeIsEnum = keyType.IsEnum;
            bool keyTypeIsInteger =
                keyType == typeof(sbyte) ||
                keyType == typeof(byte) ||
                keyType == typeof(short) ||
                keyType == typeof(ushort) ||
                keyType == typeof(int) ||
                keyType == typeof(uint) ||
                keyType == typeof(long) ||
                keyType == typeof(ulong);

            if (keyType != typeof(string) && !keyTypeIsEnum && !keyTypeIsInteger)
            {
                // We only support string, enum and integer (except nint-IntPtr and nuint-UIntPtr) keys
                return;
            }

            MethodInfo tryGetValue = dictionaryType.GetMethod("TryGetValue", DeclaredOnlyLookup)!;
            PropertyInfo indexerProperty = dictionaryType.GetProperty("Item", DeclaredOnlyLookup)!;

            foreach (IConfigurationSection child in config.GetChildren())
            {
                try
                {
                    object key = keyTypeIsEnum ? Enum.Parse(keyType, child.Key, true) :
                        keyTypeIsInteger ? Convert.ChangeType(child.Key, keyType) :
                        child.Key;

                    var valueBindingPoint = new BindingPoint(
                        initialValueProvider: () =>
                        {
                            object?[] tryGetValueArgs = { key, null };
                            return (bool)tryGetValue.Invoke(dictionary, tryGetValueArgs)! ? tryGetValueArgs[1] : null;
                        },
                        isReadOnly: false);
                    BindInstance(
                        type: valueType,
                        bindingPoint: valueBindingPoint,
                        config: child,
                        options: options,
                        true);
                    if (valueBindingPoint.HasNewValue)
                    {
                        indexerProperty.SetValue(dictionary, valueBindingPoint.Value, new object[] { key });
                    }
                }
                catch (Exception ex) when (ex is not ConfigurationValueException) // DESOMNIA: invalid values must surface
                {
                    if (options.ErrorOnUnknownConfiguration)
                    {
                        throw new InvalidOperationException(SR.Format(SR.Error_GeneralErrorWhenBinding,
                            nameof(options.ErrorOnUnknownConfiguration)), ex);
                    }
                }
            }
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode("Cannot statically analyze what the element type is of the object collection so its members may be trimmed.")]
        private static void BindCollection(
            object collection,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
            Type collectionType,
            IConfiguration config, BinderOptions options)
        {
            // ICollection<T> is guaranteed to have exactly one parameter
            Type itemType = collectionType.GenericTypeArguments[0];
            MethodInfo? addMethod = collectionType.GetMethod("Add", DeclaredOnlyLookup);

            foreach (IConfigurationSection section in config.GetChildren())
            {
                try
                {
                    BindingPoint itemBindingPoint = new();
                    BindInstance(
                        type: itemType,
                        bindingPoint: itemBindingPoint,
                        config: section,
                        options: options,
                        true);
                    if (itemBindingPoint.HasNewValue)
                    {
                        addMethod?.Invoke(collection, new[] { itemBindingPoint.Value });
                    }
                }
                catch (Exception ex) when (ex is not ConfigurationValueException) // DESOMNIA: invalid values must surface
                {
                    if (options.ErrorOnUnknownConfiguration)
                    {
                        throw new InvalidOperationException(SR.Format(SR.Error_GeneralErrorWhenBinding,
                            nameof(options.ErrorOnUnknownConfiguration)), ex);
                    }

                }
            }
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode("Cannot statically analyze what the element type is of the Array so its members may be trimmed.")]
        private static Array BindArray(Type type, IEnumerable? source, IConfiguration config, BinderOptions options)
        {
            Type elementType;
            if (type.IsArray)
            {
                if (type.GetArrayRank() > 1)
                {
                    throw new InvalidOperationException(SR.Format(SR.Error_UnsupportedMultidimensionalArray, type));
                }
                elementType = type.GetElementType()!;
            }
            else // e. g. IEnumerable<T>
            {
                elementType = type.GetGenericArguments()[0];
            }

            var list = new List<object?>();

            if (source != null)
            {
                foreach (object? item in source)
                {
                    list.Add(item);
                }
            }

            foreach (IConfigurationSection section in config.GetChildren())
            {
                var itemBindingPoint = new BindingPoint();
                try
                {
                    BindInstance(
                        type: elementType,
                        bindingPoint: itemBindingPoint,
                        config: section,
                        options: options,
                        isParentCollection: true);
                    if (itemBindingPoint.HasNewValue)
                    {
                        list.Add(itemBindingPoint.Value);
                    }
                }
                catch (Exception ex) when (ex is not ConfigurationValueException) // DESOMNIA: invalid values must surface
                {
                    if (options.ErrorOnUnknownConfiguration)
                    {
                        throw new InvalidOperationException(SR.Format(SR.Error_GeneralErrorWhenBinding,
                            nameof(options.ErrorOnUnknownConfiguration)), ex);
                    }
                }
            }

            Array result = Array.CreateInstance(elementType, list.Count);
            ((IList)list).CopyTo(result, 0);
            return result;
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode("Cannot statically analyze what the element type is of the Array so its members may be trimmed.")]
        private static object? BindSet(Type type, IEnumerable? source, IConfiguration config, BinderOptions options)
        {
            Type elementType = type.GetGenericArguments()[0];

            bool elementTypeIsEnum = elementType.IsEnum;

            if (elementType != typeof(string) && !elementTypeIsEnum)
            {
                // We only support string and enum keys
                return null;
            }

            object?[] arguments = new object?[1];
            // addMethod can only be null if type is IReadOnlySet<T> rather than ISet<T>.
            MethodInfo? addMethod = type.GetMethod("Add", DeclaredOnlyLookup);
            if (addMethod is null || source is null)
            {
                Type genericType = typeof(HashSet<>).MakeGenericType(elementType);
                object instance = Activator.CreateInstance(genericType)!;
                addMethod = genericType.GetMethod("Add", DeclaredOnlyLookup);

                if (source != null)
                {
                    foreach (object? item in source)
                    {
                        arguments[0] = item;
                        addMethod!.Invoke(instance, arguments);
                    }
                }

                source = (IEnumerable)instance;
            }

            Debug.Assert(source is not null);
            Debug.Assert(addMethod is not null);

            foreach (IConfigurationSection section in config.GetChildren())
            {
                var itemBindingPoint = new BindingPoint();
                try
                {
                    BindInstance(
                        type: elementType,
                        bindingPoint: itemBindingPoint,
                        config: section,
                        options: options,
                        true);
                    if (itemBindingPoint.HasNewValue)
                    {
                        arguments[0] = itemBindingPoint.Value;

                        addMethod.Invoke(source, arguments);
                    }
                }
                catch (Exception ex) when (ex is not ConfigurationValueException) // DESOMNIA: invalid values must surface
                {
                    if (options.ErrorOnUnknownConfiguration)
                    {
                        throw new InvalidOperationException(SR.Format(SR.Error_GeneralErrorWhenBinding,
                            nameof(options.ErrorOnUnknownConfiguration)), ex);
                    }
                }
            }

            return source;
        }

        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        private static bool TryConvertValue(
            Type type,
            string? value, string? path, out object? result, out Exception? error)
        {
            error = null;
            result = null;
            if (type == typeof(object))
            {
                result = value;
                return true;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (string.IsNullOrEmpty(value))
                {
                    return true;
                }
                return TryConvertValue(Nullable.GetUnderlyingType(type)!, value, path, out result, out error);
            }

            // DESOMNIA: accept user-friendly value variations, applied type-aware
            // (formerly rewritten blindly on the XML attribute level by the provider)
            if (!string.IsNullOrEmpty(value))
            {
                if (type.IsEnum)
                {
                    value = ValueVariations.NormalizeEnum(value);
                }
                else if (type == typeof(TimeSpan))
                {
                    value = ValueVariations.NormalizeTimeSpan(value);
                }
            }

            TypeConverter converter = TypeDescriptor.GetConverter(type);
            if (converter.CanConvertFrom(typeof(string)))
            {
                try
                {
                    if (value is not null)
                    {
                        result = converter.ConvertFromInvariantString(value);
                    }
                }
                catch (Exception ex)
                {
                    // DESOMNIA: distinct exception type, rethrown from collection binding
                    error = new ConfigurationValueException(SR.Format(SR.Error_FailedBinding, value, path, type), ex);
                }
                return true;
            }

            if (type == typeof(byte[]))
            {
                try
                {
                    if (value is not null)
                    {
                        result = value == string.Empty ? Array.Empty<byte>() : Convert.FromBase64String(value);
                    }
                }
                catch (FormatException ex)
                {
                    // DESOMNIA: distinct exception type, rethrown from collection binding
                    error = new ConfigurationValueException(SR.Format(SR.Error_FailedBinding, value, path, type), ex);
                }
                return true;
            }

            return false;
        }

        [RequiresUnreferencedCode(TrimmingWarningMessage)]
        private static object? ConvertValue(
            Type type,
            string value, string? path)
        {
            TryConvertValue(type, value, path, out object? result, out Exception? error);
            if (error != null)
            {
                throw error;
            }
            return result;
        }

        // DESOMNIA: convention support for an element's text content: a complex type declaring
        // a public constructor with a single string parameter receives the section value there
        // and can process it further itself (replaces the synthetic "text" attribute and the
        // "Text" carrier properties on the config classes).
        private static ConstructorInfo? FindTextConstructor(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
            Type type)
        {
            if (type.IsAbstract || type.IsInterface)
                return null;

            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                if (constructor.GetParameters() is [ParameterInfo parameter] && parameter.ParameterType == typeof(string))
                {
                    return constructor;
                }
            }

            return null;
        }

        private static object InvokeTextConstructor(ConstructorInfo constructor, string text, IConfigurationSection? section)
        {
            try
            {
                return constructor.Invoke([text]);
            }
            catch (TargetInvocationException ex) // the constructor rejected the value
            {
                throw new ConfigurationValueException(SR.Format(SR.Error_FailedBinding, text, section?.Path, constructor.DeclaringType!), ex.InnerException ?? ex);
            }
        }

        private static bool TypeIsADictionaryInterface(Type type)
        {
            if (!type.IsInterface || !type.IsConstructedGenericType) { return false; }

            Type genericTypeDefinition = type.GetGenericTypeDefinition();
            return genericTypeDefinition == typeof(IDictionary<,>)
                || genericTypeDefinition == typeof(IReadOnlyDictionary<,>);
        }

        private static bool IsImmutableArrayCompatibleInterface(Type type)
        {
            if (!type.IsInterface || !type.IsConstructedGenericType) { return false; }

            Type genericTypeDefinition = type.GetGenericTypeDefinition();
            return genericTypeDefinition == typeof(IEnumerable<>)
                || genericTypeDefinition == typeof(IReadOnlyCollection<>)
                || genericTypeDefinition == typeof(IReadOnlyList<>);
        }

        private static bool IsIEnumerableInterface(Type type)
            => type.IsInterface && type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);

        private static bool TypeIsASetInterface(Type type)
        {
            if (!type.IsInterface || !type.IsConstructedGenericType) { return false; }

            Type genericTypeDefinition = type.GetGenericTypeDefinition();
            return genericTypeDefinition == typeof(ISet<>)
                   || genericTypeDefinition == typeof(IReadOnlySet<>);
        }

        private static Type? FindOpenGenericInterface(
            Type expected,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
            Type actual)
        {
            if (actual.IsGenericType &&
                actual.GetGenericTypeDefinition() == expected)
            {
                return actual;
            }

            Type[] interfaces = actual.GetInterfaces();
            foreach (Type interfaceType in interfaces)
            {
                if (interfaceType.IsGenericType &&
                    interfaceType.GetGenericTypeDefinition() == expected)
                {
                    return interfaceType;
                }
            }
            return null;
        }

        private static List<PropertyInfo> GetAllProperties(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.AllProperties)]
            Type type)
        {
            var allProperties = new List<PropertyInfo>();

            Type baseType = type;
            while (baseType != typeof(object))
            {
                PropertyInfo[] properties = baseType.GetProperties(DeclaredOnlyLookup);

                foreach (PropertyInfo property in properties)
                {
                    // if the property is virtual, only add the base-most definition so
                    // overridden properties aren't duplicated in the list.
                    MethodInfo? setMethod = property.GetSetMethod(true);

                    if (setMethod is null || !setMethod.IsVirtual || setMethod == setMethod.GetBaseDefinition())
                    {
                        allProperties.Add(property);
                    }
                }

                baseType = baseType.BaseType!;
            }

            return allProperties;
        }

        [RequiresDynamicCode(DynamicCodeWarningMessage)]
        [RequiresUnreferencedCode(PropertyTrimmingWarningMessage)]
        private static object? BindParameter(ParameterInfo parameter, Type type, IConfiguration config,
            BinderOptions options)
        {
            string? parameterName = parameter.Name;

            if (parameterName is null)
            {
                throw new InvalidOperationException(SR.Format(SR.Error_ParameterBeingBoundToIsUnnamed, type));
            }

            var propertyBindingPoint = new BindingPoint(initialValue: config.GetSection(parameterName).Value, isReadOnly: false);

            BindInstance(
                parameter.ParameterType,
                propertyBindingPoint,
                config.GetSection(parameterName),
                options,
                false);

            if (propertyBindingPoint.Value is null)
            {
                if (TryGetParameterDefaultValue(parameter, out object? defaultValue))
                {
                    propertyBindingPoint.SetValue(defaultValue);
                }
                else
                {
                    // DESOMNIA: a constructor parameter that cannot be filled from config is a
                    // configuration error and must surface from collection binding (e.g. a type
                    // whose only constructor takes mandatory element text content)
                    throw new ConfigurationValueException(SR.Format(SR.Error_ParameterHasNoMatchingConfig, type, parameterName));
                }
            }

            return propertyBindingPoint.Value;
        }

        // Replaces the internal Microsoft.Extensions.Internal.ParameterDefaultValue helper.
        private static bool TryGetParameterDefaultValue(ParameterInfo parameter, out object? defaultValue)
        {
            bool hasDefaultValue;
            try
            {
                hasDefaultValue = parameter.HasDefaultValue;
            }
            catch (FormatException)
            {
                hasDefaultValue = false; // https://github.com/dotnet/runtime/issues/17843
            }

            defaultValue = null;

            if (hasDefaultValue)
            {
                defaultValue = parameter.DefaultValue;

                // Workaround for https://github.com/dotnet/runtime/issues/18599:
                // reflection reports default values of value-typed parameters as null
                if (defaultValue is null && parameter.ParameterType.IsValueType &&
                    Nullable.GetUnderlyingType(parameter.ParameterType) is null)
                {
                    defaultValue = Activator.CreateInstance(parameter.ParameterType);
                }
            }

            return hasDefaultValue;
        }

        private static string GetPropertyName(PropertyInfo property)
        {
            ArgumentNullException.ThrowIfNull(property);

            // Check for a custom property name used for configuration key binding
            foreach (var attributeData in property.GetCustomAttributesData())
            {
                if (attributeData.AttributeType != typeof(ConfigurationKeyNameAttribute))
                {
                    continue;
                }

                // Ensure ConfigurationKeyName constructor signature matches expectations
                if (attributeData.ConstructorArguments.Count != 1)
                {
                    break;
                }

                // Assumes ConfigurationKeyName constructor first arg is the string key name
                string? name = attributeData
                    .ConstructorArguments[0]
                    .Value?
                    .ToString();

                return !string.IsNullOrWhiteSpace(name) ? name : property.Name;
            }

            return property.Name;
        }

        // Message texts vendored from the binder's Strings.resx.
        private static class SR
        {
            public const string Error_CannotActivateAbstractOrInterface = "Cannot create instance of type '{0}' because it is either abstract or an interface.";
            public const string Error_CannotBindToConstructorParameter = "Cannot create instance of type '{0}' because one or more parameters cannot be bound to. Constructor parameters cannot be declared as in, out, or ref. Invalid parameters are: '{1}'";
            public const string Error_ConstructorParametersDoNotMatchProperties = "Cannot create instance of type '{0}' because one or more parameters cannot be bound to. Constructor parameters must have corresponding properties. Fields are not supported. Missing properties are: '{1}'";
            public const string Error_FailedBinding = "Failed to convert configuration value '{0}' at '{1}' to type '{2}'.";
            public const string Error_FailedToActivate = "Failed to create instance of type '{0}'.";
            public const string Error_GeneralErrorWhenBinding = "'{0}' was set and binding has failed. The likely cause is an invalid configuration value.";
            public const string Error_MissingConfig = "'{0}' was set on the provided {1}, but the following properties were not found on the instance of {2}: {3}";
            public const string Error_MissingPublicInstanceConstructor = "Cannot create instance of type '{0}' because it is missing a public instance constructor.";
            public const string Error_MultipleParameterizedConstructors = "Cannot create instance of type '{0}' because it has multiple public parameterized constructors.";
            public const string Error_ParameterBeingBoundToIsUnnamed = "Cannot create instance of type '{0}' because one or more parameters are unnamed.";
            public const string Error_ParameterHasNoMatchingConfig = "Cannot create instance of type '{0}' because parameter '{1}' has no matching config. Each parameter in the constructor that does not have a default value must have a corresponding config entry.";
            public const string Error_UnsupportedMultidimensionalArray = "Cannot create instance of type '{0}' because multidimensional arrays are not supported.";

            public static string Format(string format, params object?[] args) => string.Format(format, args);
        }
    }
}
