﻿using Azure;
using Core.Interfaces;
using Core.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ihelpers.Helpers;
using Ihelpers.Interfaces;
using Ihelpers.DataAnotations;
using Ihelpers.Extensions;
using Idata.Helpers;
using Idata.Entities.Core;

namespace Core.Transformers
{
    public static class TransformerBase
    {
        /// <summary>
        /// Asynchronous transform of a set of items of type List[TEntity] to a camelCased List
        /// </summary>
        /// <param name="listToTransform">List[TEntity] support only</param>
        /// <returns>returns a List of dictionaries containing all properties camel cased</returns>
        public static async Task<List<dynamic>> TransformCollection(dynamic listToTransform)
        {
            List<dynamic> list = new List<dynamic>();
            //verify if input is a list
            if (TypeHelper.isList(listToTransform))
            {
                //Foreach item in input list
                foreach (dynamic itemToTransform in listToTransform)
                {
                    //internal dictionary to be populated
                    var dic = new Dictionary<string, dynamic?>();

                    //Analize item and add transformed properties to dictionary
                    await Analyze(itemToTransform, dic);

                    //Add the dictionary to the result list
                    list.Add(dic);
                }

            }
            //return the list
            return list;
        }

        /// <summary>
        /// Asynchronous transform of a set of items of type List[TEntity] to a camelCased List, having in mind a cached data if needed
        /// </summary>
        /// <param name="listToTransform">List[TEntity] support only</param>
        /// <param name="cache">Cached data to be used</param>
        /// <returns>returns a List of dictionaries containing all properties camel cased</returns>
        public static async Task<List<dynamic>> TransformCollection(dynamic listToTransform, ICacheBase? cache = null)
        {
            List<dynamic> list = new List<dynamic>();
            //verify if input is a list
            if (TypeHelper.isList(listToTransform))
            {
                //Foreach item in input list
                foreach (dynamic itemToTransform in listToTransform)
                {
                    //internal dictionary to be populated
                    var dic = new Dictionary<string, dynamic?>();

                    //Analize item and add transformed properties to dictionary
                    await Analyze(itemToTransform, dic, null, cache);

                    //Add the dictionary to the result list
                    list.Add(dic);
                }

            }
            //return the list
            return list;
        }

        /// <summary>
        /// Asynchronous transform of a set of items of type List[TEntity] to a camelCased List, having in mind a cached data if needed and user timezone for date conversion
        /// </summary>
        /// <param name="listToTransform">List[TEntity] support only</param>
        /// <param name="cache">Cached data to be used</param>
        /// <param name="userTimezone">Timezone to apply to the Date Fields</param>
        /// <returns>returns a List of dictionaries containing all properties camel cased</returns>
        public static async Task<List<dynamic>> TransformCollection(dynamic listToTransform, ICacheBase? cache = null, string? userTimezone = "00:00")
        {
            List<dynamic> list = new List<dynamic>();
            //verify if input is a list
            if (TypeHelper.isList(listToTransform))
            {
                //Foreach item in input list
                foreach (dynamic itemToTransform in listToTransform)
                {
                    //internal dictionary to be populated
                    var dic = new Dictionary<string, dynamic?>();

                    //Analize item and add transformed properties to dictionary
                    await Analyze(itemToTransform, dic, null, cache, userTimezone);

                    //Add the dictionary to the result list
                    list.Add(dic);
                }

            }
            //return the list
            return list;
        }

        /// <summary>
        /// Asynchronous transform of an item of type TEntity to a camelCased entity
        /// </summary>
        /// <param name="objectToTransform">TEntity object to be transformed</param>
        /// <returns>returns a dictionary containing all properties camel cased</returns>
        public static async Task<Dictionary<string, dynamic?>> TransformItem(dynamic objectToTransform)
        {
            //internal dictionary to be populated
            var dic = new Dictionary<string, dynamic?>();

            //verify if input is of type EntityBase

            //Analize item and add transformed properties to dictionary
            await Analyze(objectToTransform, dic);

            //return the dictionary with properties and values
            return dic;
        }

        /// <summary>
        /// Asynchronous transform of an item of type TEntity to a camelCased entity having in mind a cached data if needed 
        /// </summary>
        /// <param name="objectToTransform">TEntity object to be transformed</param>
        /// <param name="cache">Cached data to be used</param>
        /// <returns>returns a dictionary containing all properties camel cased</returns>
        public static async Task<Dictionary<string, dynamic?>> TransformItem(dynamic objectToTransform, ICacheBase? cache = null)
        {
            //internal dictionary to be populated
            var dic = new Dictionary<string, dynamic?>();

            //verify if input is of type EntityBase

            //Analize item and add transformed properties to dictionary
            await Analyze(objectToTransform, dic, null, cache);

            //return the dictionary with properties and values
            return dic;
        }

        /// <summary>
        /// Asynchronous transform of an item of type TEntity to a camelCased entity having in mind a cached data if needed and user timezone
        /// </summary>
        /// <param name="objectToTransform">TEntity object to be transformed</param>
        /// <param name="cache">Cached data to be used</param>
        /// <param name="userTimezone">Timezone to apply to the Date Fields</param>
        /// <returns>returns a dictionary containing all properties camel cased</returns>
        public static async Task<Dictionary<string, dynamic?>> TransformItem(dynamic objectToTransform, ICacheBase? cache = null, string? userTimezone = "00:00")

        /// <summary>
        /// Actual method that converts any object to a dictionary camel cased properties as keys0:00")
        {
            //internal dictionary to be populated
            var dic = new Dictionary<string, dynamic?>();

            //verify if input is of type EntityBase

            //Analize item and add transformed properties to dictionary
            await Analyze(objectToTransform, dic, null, cache, userTimezone);

            //return the dictionary with properties and values
            return dic;
        }

        
        /// </summary>
        /// <param name="objectToTransform">The object to be transformed (of type EntityBase)</param>
        /// <param name="dic">dictionary that will hold the object conversion data</param>
        /// <param name="invokerClass">Indicates when this call is recursive or not</param>
        /// <param name="cache">For handling cache data if needed</param>
        /// <param name="userTimezone">User timezone to apply to Date type properties</param>
        /// <returns>Modifies the dictionary</returns>
        public static async Task Analyze(dynamic objectToTransform, Dictionary<string, dynamic?> dic, string? invokerClass = null, ICacheBase? cache = null, string userTimezone = "0:00")
        {
            //Apply reflection to get object properties
            var objectProperties = objectToTransform.getProperties();
            //Handle timezone
            bool handleTimezone = false;
            //TODO read all configs from cache
            bool.TryParse(Ihelpers.Helpers.ConfigurationHelper.GetConfig("DefaultConfigs:TimezoneHandle"), out handleTimezone);
            //get dateFormats
            var dateTimeFormat = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string[]>("DefaultConfigs:DateFormats");

            //iterate over every object property
            foreach (System.Reflection.PropertyInfo? prop in objectProperties)
            {
                //if Analyze was invoked due to a relational field, the invoker parameter will contain
                //the parent(invoker) class name 
                if (invokerClass != null)
                {
                    //convert parent class name to lowercase to prevent mismatching
                    var invokerClassName = invokerClass.ToLower();
                    var propNameLowered = prop.Name.ToLower();

                    //prevent infiniteLoop references in many to many relations
                    if (propNameLowered == invokerClassName || propNameLowered == invokerClassName + "s" || propNameLowered == invokerClassName + "es" || propNameLowered == "dynamic_parameters"
                        || propNameLowered + "s" == invokerClassName || propNameLowered + "es" == invokerClassName)
                    {
                        continue;
                    }
                }

                //Get the Data Anotations that are valid and were not mark as Ignore into an array
                dynamic[] attrs = prop.GetCustomAttributes(true).Where(att => att != null && att is not Ignore && att.ToString().Contains("Nullable") == false && att is IDataAnnotationBase).ToArray();
                //If property is set to ignore, ignere it inside transformer
                var ignoreAttr = prop.GetCustomAttributes(true).Where(att => att != null && att is Ignore && att is IDataAnnotationBase).FirstOrDefault();
                if (ignoreAttr != null) continue;
                //get the property name
                string propertyName = StringHelper.ToCamelCase(prop.Name);

                //get the property value
                dynamic? value = await ClassHelper.GetValObjDy(objectToTransform, prop.Name);

                //If is datetime and parameter is set to true
                if (prop.PropertyType.FullName.Contains("System.DateTime") && value != null)
                {

                    // Check if handleTimezone flag is true
                    if (handleTimezone)
                    {
                        // Get the date from the value object
                        DateTime date = (DateTime)value;

                        // Check if the NoUserTimezone data annotation is present
                        var noConvertToUserTimezone = attrs.Where(att => att is NoUserTimezone).FirstOrDefault();

                        // Get the time offset from the userTimezone string
                        TimeSpan dateOffset = TimeSpan.Parse(userTimezone);

                        // If the NoUserTimezone data annotation is not present, add the time offset to the date
                        // Else, keep the date as is
                        value = noConvertToUserTimezone == null ? date.Add(dateOffset) : date;

                        // Add the property name and updated value to the dictionary
                        dic.Add(propertyName, value);

                        // Continue to the next iteration
                        continue;
                    }

                }
                //if the property doesn't have data anotation transform it to camel case by default
                if ((attrs.Length == 0 && propertyName != "translations") || value == null)
                {
                    //Add the property name and value to the dictionary
                    dic.Add(propertyName, value);

                    // Continue to the next iteration
                    continue;
                }


                //When transforming to front hide the password property

                var isPasswordField = attrs.Where(att => att is Password).FirstOrDefault() != null;
                if (isPasswordField)
                {
                    dic.Add(propertyName, "***");
                    continue;
                }



                //Loop trough attributes 
                foreach (Attribute attr in attrs)
                {
                    // Check if the current attribute is of type ObjectToString
                    if (attr is ObjectToString && attr != null)
                    {
                        // Convert the property name to camel case
                        string propName = StringHelper.ToCamelCase(prop.Name);

                        // Get the value of the corresponding property of the object
                        dynamic? propValue = await ClassHelper.GetValObjDy(objectToTransform, prop.Name);

                        // Initialize a dictionary to store the JSON object
                        Dictionary<string, object?> JsonObject = new();

                        // Try to convert the property value into a JSON object
                        if (propValue != null)
                        {
                            try
                            {
                                // Deserialize the property value into a dictionary
                                JsonObject = JsonConvert.DeserializeObject<Dictionary<string, object?>>(propValue.ToString().Replace("'", "\""));
                            }
                            catch (Exception ex)
                            {
                                // If an exception occurs, parse the property value into a JSON array
                                JArray ArrayValue = JArray.Parse(propValue.ToString());

                                // Add the array to the main dictionary with the property name as the key
                                dic.Add(propertyName, ArrayValue);

                                // Continue to the next iteration of the loop
                                continue;
                            }
                        }

                        // Add the JSON object to the main dictionary with the property name as the key
                        dic.Add(propertyName, JsonObject);
                    }

                    // Check if the current attribute is of type SimpleObjectToString
                    if (attr is SimpleObjectToString && attr != null)
                    {
                        // Convert the property name to camel case
                        string propName = StringHelper.ToCamelCase(prop.Name);

                        // Get the value of the corresponding property of the object
                        dynamic? propValue = await ClassHelper.GetValObjDy(objectToTransform, prop.Name);

                        // Initialize an object to store the JSON value
                        object? JsonObject = new();

                        // Try to convert the property value into a simple JSON object
                        if (propValue != null)
                        {
                            try
                            {
                                // Deserialize the property value into an object
                                JsonObject = JsonConvert.DeserializeObject<object?>(propValue.ToString().Replace("'", "\""));
                            }
                            catch
                            {
                                // If an exception occurs, do nothing
                            }
                        }

                        // Add the JSON value to the main dictionary with the property name as the key
                        dic.Add(propertyName, JsonObject);
                    }

                    //if the attribute is marked as relational field
                    if (attr is RelationalField)
                    {


                        //verify if the relational property is a list
                        if (TypeHelper.isList(value))
                        {
                            //if the property is a list if will be analyzed and transformed too, and stored inside another list
                            List<dynamic> dynamics = new List<dynamic>();

                            //for each item in the list, transform it and add to the list
                            foreach (dynamic item in value)
                            {
                                //Is necesary identify the root class to avoid ciclic redundant infinite loop on many to many relationships

                                string className = await TypeHelper.getClassName(objectToTransform);

                                dynamics.Add(await TransformRecursive(item, className));
                            }
                            //if the property is a translation must be added with the locale as Key and root
                            if (propertyName == "translations")
                            {
                                //iterate trough the list and add the item to the root json
                                foreach (dynamic item in dynamics)
                                {
                                    var dictionary = (Dictionary<string, dynamic?>)item;

                                    //[traduction]
                                    dynamic locale = dictionary["locale"];

                                    dic.Add(locale, item);

                                }
                            }
                            else
                            {
                                //if the list of item aren't translations, simply add it to the dictionary
                                dic.Add(propertyName, dynamics);
                            }

                        }
                        else
                        {
                            //if is not a list the item doesn't need aditional 
                            if (propertyName != "translations")
                            {

                                //Is necesary identify the root class to avoid ciclic redundant infinite loop on many to many relationships
                                string className = objectToTransform.getClasssName().ToLower();
                                dic.Add(propertyName, await TransformRecursive(value, className));


                            }


                        }

                    }


                }


            }



        }

        /// <summary>
        /// Converts a json string to a specific class (unknowObject) taking care about relations 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="JsonString"></param>
        /// <returns></returns>
        public static async Task<T> ToClass<T>(string? JsonString) where T : EntityBase
        {
            //Deserialize object into a usable dictionary class
            dynamic? propertiesAndValues = JsonConvert.DeserializeObject<Dictionary<string, dynamic?>>(JsonString);

            //Pass the dictionary and the type to base convert method
            var returnObject = await ConverToClass(propertiesAndValues, typeof(T));


            //Cast to anytype  (T) class
            return (T)returnObject;
        }

        /// <summary>
        /// Converts a json string to a specific class (unknowObject) taking care about relations and cached parameters
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="JsonString"></param>
        /// <returns></returns>
        public static async Task<T> ToClass<T>(string? JsonString, ICacheBase cache) where T : EntityBase
        {
            //Deserialize object into a usable dictionary class
            dynamic? propertiesAndValues = JsonConvert.DeserializeObject<Dictionary<string, dynamic?>>(JsonString);


            var returnObject = await ConverToClass(propertiesAndValues, typeof(T), cache);
            //Cast to anytype  (T) class
            return (T)returnObject;
        }

        /// <summary>
        /// Converts a json string to a specific class (unknowObject) taking care about relations, cached parameters and user timezone
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="JsonString"></param>
        /// <returns></returns>
        public static async Task<T> ToClass<T>(string? JsonString, ICacheBase? cache = null, string? userTimezone = "00:00") where T : EntityBase
        {
            dynamic? propertiesAndValues = JsonConvert.DeserializeObject<Dictionary<string, dynamic?>>(JsonString);

            var returnObject = await ConverToClass(propertiesAndValues, typeof(T), cache, userTimezone);
            //Cast to anytype  (T) class
            return (T)returnObject;
        }


        /// <summary>
        /// Converts a json string to a cached class (unknowObject) obtaining its type from cache and taking care about relations, cached parameters and user timezone
        /// </summary>
        /// <typeparam name="entityPath">The entity path inside cache</typeparam>
        /// <param name="JsonString">The json to parse class</param>
        /// <returns>The new object </returns>
        public static async Task<object> ToClassInCache(string? JsonString, string? entityPath, string? userTimezone = "00:00")
        {
            var allTypes = ConfigContainer.cache.GetValue<List<Type>>("AllClasses");

            if (allTypes == null)
            {
                throw new KeyNotFoundException("Key AllClasses not found in cache");
            }

            //Construct path
            string path;
            Type type;

            if (entityPath.Contains('.'))
            {
                path = entityPath;

                type = allTypes.FirstOrDefault(ac => ac.FullName == entityPath);
            }
            else
            {
                var pathSplit = entityPath.Split('\\');

                path = $"{pathSplit[1]},{pathSplit.Last()}";

                type = allTypes.FirstOrDefault(ac => ac.FullName.Contains(pathSplit[1]) && ac.FullName.Contains(pathSplit.Last()));
            }

            //Deserialize Item from property to Type of entity
            var obj = JsonConvert.DeserializeObject(JsonString, type);

            var returnObject = await TransformItem(obj);

            //Cast to anytype  (T) class
            return returnObject;
        }

        /// <summary>
        /// Calls an internal method and them convert the result to specified class type
        /// </summary>
        /// <typeparam name="T">The target class type</typeparam>
        /// <param name="dict">The dictionary that contains the object values</param>
        /// <returns></returns>
        public static async Task<T> ToClass<T>(Dictionary<string, dynamic?> dict)
        {
            var returnObject = await ConverToClass(dict, typeof(T));
            //Cast to anytype  (T) class
            return (T)returnObject;
        }

        /// <summary>
        /// Internal method that converts a Dictionary<string, dynamic> to a specified class, matching dictionary keys with properties and dictionary values with class values
        /// Supports nested objects inside dictionary
        /// </summary>
        /// <param name="dic">Dictionary of properties and values</param>
        /// <param name="classToUse">Type of target class</param>
        /// <param name="cache">Cache if needed</param>
        /// <param name="userTimezone"></param>
        /// <returns></returns>
        private static async Task<object> ConverToClass(Dictionary<string, dynamic?> dic, Type classToUse, ICacheBase? cache = null, string userTimezone = "00:00")
        {

            Type type = classToUse;

            //TODO: Implement cache

            bool handleTimezone = false;

            bool.TryParse(Ihelpers.Helpers.ConfigurationHelper.GetConfig("DefaultConfigs:TimezoneHandle"), out handleTimezone);

            var dateTimeFormat = Ihelpers.Helpers.ConfigurationHelper.GetConfig<string[]>("DefaultConfigs:DateFormats");

            dynamic? obj = Activator.CreateInstance(type);

            List<System.Reflection.PropertyInfo?> properties = obj.getProperties();

            foreach (var item in dic)
            {

                //Case when front sent property is the exact property name for example property "id" 
                var property = type.GetProperty(item.Key);

                if (property == null)
                {
                    //Case when front send property_name_sent convert it to propertyNameSent and try match with model
                    //Common with relations that are list
                    property = type.GetProperty(item.Key.ToCamelCase());
                }

                //If the property doesn't belong to the class skip it
                if (property == null) continue;

                if (classToUse.BaseType.FullName.Contains("EntityBase") && item.Value != null)
                {
                    var plainStringValue = item.Value.ToString();

                    object? value = item.Value;

                    //set null if is sent "" as value by front
                    if (string.IsNullOrEmpty(plainStringValue))
                    {
                        property.SetValue(obj, null, null);
                        continue;
                    }

                    if (value is Dictionary<string, object> && !property.PropertyType.FullName.Contains("Generic.IList"))
                    {
                        property.SetValue(obj, await ConverToClass((Dictionary<string, object>)(item.Value), property.PropertyType));
                        continue;
                    }

                    var prop = properties.Where(p => p.Name == item.Key).FirstOrDefault();

                    if (prop == null)
                    {
                        prop = properties.Where(p => p.Name == item.Key.ToCamelCase()).FirstOrDefault();
                    }
                    //Get the Data Anotations that are valid and were not mark as Ignore into an array
                    dynamic[] attrs = prop.GetCustomAttributes(true).Where(att => att != null && att is not Ignore && att.ToString().Contains("Nullable") == false && att is IDataAnnotationBase).ToArray();
                    //if the property doesn't have data anotation transform it to camel case by default
                    if (attrs.Length == 0)
                    {
                        //Convert the value to match the type of property
                        Type t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                        if (property.PropertyType.IsEnum)
                        {
                            property.SetValue(obj, Enum.Parse(property.PropertyType, value.ToString()));
                        }

                        //Set DateTime fields  to current UTC time
                        else if (t.Name == "DateTime"
                            && property.Name != "deleted_at"
                            && property.Name != "created_at"
                            && property.Name != "updated_at")
                        {
                            if (handleTimezone && value is not null)
                            {
                                DateTime dateSent;

                                if (dateTimeFormat != null)
                                {
                                    //If specific format was set use it
                                    dateSent = DateTime.ParseExact(item.Value.ToString(), dateTimeFormat, null);
                                }
                                else
                                {
                                    //Get date sent by frontend
                                    dateSent = DateTime.Parse(item.Value.ToString());
                                }


                                //Get raw value
                                var rawUserTimezone = TimeSpan.Parse(userTimezone).Negate();

                                //convert to UTC
                                dateSent = dateSent.Add(rawUserTimezone);

                                //Set the UTC value to the property
                                property.SetValue(obj, dateSent, null);
                            }
                            else
                            {
                                //Create the value of the type 
                                object propValueWithType = (value == null) ? null : Convert.ChangeType(value, t);

                                //Add the property value to the object 
                                property.SetValue(obj, propValueWithType, null);
                            }


                        }
                        //String type doesn't implement Iconvertible so casting will result in an exception
                        else if (t.Name == "String")
                        {
                            property.SetValue(obj, plainStringValue, null);
                        }
                        else if (t.Name == "TimeSpan")
                        {
                            try
                            {
                                TimeSpan timesPanValue = TimeSpan.Parse(plainStringValue);
                                property.SetValue(obj, timesPanValue, null);
                            }

                            catch { CoreLogger.LogMessage($"Transformer warning: Value {plainStringValue} for property:{property.Name} of {type.Name} Entity is not valid!", logType: LogType.Warning); }


                        }
                        else
                        {
                            //Create the value of the type 
                            object propValueWithType = (value == null) ? null : Convert.ChangeType(value, t);

                            //Add the property value to the object 
                            property.SetValue(obj, propValueWithType, null);
                        }

                        //continue to next property (first for bucle)

                    }
                    else
                    {
                        foreach (Attribute attr in attrs)
                        {
                            if (attr is ObjectToString || attr is SimpleObjectToString)
                            {

                                var formattedStringValue = string.Join(String.Empty, Regex.Split(plainStringValue, @"(?:\r\n|\n|\r)"));

                                formattedStringValue = formattedStringValue.Replace("\"", "'"); // "   '

                                property.SetValue(obj, formattedStringValue);

                                break;
                            }
                            else if (attr is Password)
                            {
                                property.SetValue(obj, await EncryptionHelper.Encrypt(plainStringValue));
                            }
                            else if (attr is RelationalField)
                            {
                                //Get the internal parameters field if exists
                                dynamic? internal_relations = null;

                                //obj.dynamic_parameters.Add("relations", new Dictionary<string, List<dynamic?>>());

                                //obj.dynamic_parameters["relations"].Add(prop.Name, new List<dynamic>());

                                //obj.dynamic_parameters["relations"][prop.Name].Add(item.Value);

                                obj.dynamic_parameters.TryGetValue("relations", out internal_relations);



                                if (internal_relations == null)
                                {
                                    obj.dynamic_parameters.Add("relations", new Dictionary<string, List<long?>>());

                                    obj.dynamic_parameters["relations"].Add(prop.Name, new List<long?>());

                                    //List to store ids
                                    List<long?> ids = new();

                                    //Add the ids to the list
                                    foreach (var id in item.Value)
                                        ids.Add(Convert.ToInt64(id.Value));

                                    //Add the relations id to the object
                                    obj.dynamic_parameters["relations"][prop.Name].AddRange(ids);
                                }
                                else
                                {

                                    List<long?> propertiesValues = null;

                                    //List to store ids
                                    List<long?> ids = new();

                                    //Add the ids to the list
                                    foreach (var id in item.Value)
                                        ids.Add(Convert.ToInt64(id.Value));

                                    internal_relations.TryGetValue(prop.Name, out propertiesValues);

                                    if (propertiesValues == null)
                                    {
                                        internal_relations.Add(prop.Name, new List<long?>());

                                        internal_relations[prop.Name].AddRange(ids);
                                    }
                                    else
                                    {
                                        internal_relations[prop.Name].Add(ids);
                                    }
                                }

                                //When property list is explicit sent like "property_relational_that_is_list
                                //this method will handle it, else it will handle by dynamic_parameters with sync_relations method
                                if (property.PropertyType.FullName.Contains("Generic.List") && item.Key.Contains('_'))
                                {
                                    var subClassTouse = property.PropertyType.GetGenericArguments()[0];

                                    Type genericListType = typeof(List<>);

                                    Type concreteListType = genericListType.MakeGenericType(subClassTouse);

                                    var list = (IList)Activator.CreateInstance(concreteListType, new object[] { });

                                    var listValues = JsonConvert.DeserializeObject(dic[item.Key].ToString(), concreteListType);

                                    property.SetValue(obj, listValues, null);
                                }

                            }

                        }

                    }
                    continue;
                }
                property.SetValue(obj, item.Value);
            }

            return obj;
        }


        //Only used for internal recursive calls
        private static async Task<Dictionary<string, dynamic?>> TransformRecursive(dynamic objectToTransform, string? invokerName = null, ICacheBase? cache = null, string userTimezone = "0:00")
        {
            //internal dictionary to be populated
            var dic = new Dictionary<string, dynamic?>();

            //verify if input is of type EntityBase

            //Analize item and add transformed properties to dictionary
            await Analyze(objectToTransform, dic, invokerName, cache);

            //return the dictionary with properties and values
            return dic;
        }


        public static async Task<string> GetCSVReport<T>(object entities, Dictionary<string, string> pathFields = null)
        {

            //avoid selfReferenceLoop error
            JsonSerializerSettings settings = new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

            string jsonResponse = JsonConvert.SerializeObject(entities, settings);

            //Initialice JsonString and JArray
            var jsonArray = JArray.Parse(jsonResponse);

            var json = "[";

            StringBuilder response = new StringBuilder();
            response.AppendLine(typeof(T).Name);

            //Add headers
            response.AppendLine(string.Join('|', pathFields.Keys.ToList()));

            foreach (var line in jsonArray)
            {

                //Cast line to JObject to be able to use Descendants()
                var internalEntity = (JObject)line;

                var internalKeys = pathFields.Keys.ToList();

                //Get from last one to first and construct its path as jsonkey and value as json value
                string[]? result = internalEntity.Descendants()
                  .Where(t => !t.HasValues)
                  .Select(t => "\"" + t.Path.WithoutPrefix() + "\"" + " : " + "\"" + t.ToString() + "\",")
                  .ToArray();

                //cut off the last ","
                result[result.Length - 1] = result[result.Length - 1]
                                          .Substring(0, result[result.Length - 1].Length - 1);
                if (pathFields != null)
                {
                    //Only selected fields


                    for (int i = 0; i <= result.Length - 1; i++)
                    {
                        bool keep = false;


                        for (int pathIndex = 0; pathIndex <= internalKeys.Count - 1; pathIndex++)
                        {
                            string path = internalKeys[pathIndex];

                            string resultPath = result[i].Unformatted().Trim().Replace("\"", string.Empty);

                            if (resultPath.HasPath(path))
                            {
                                keep = true;
                                //remove encountered field from list to avoid research on same entity
                                //Only if not list
                                if (!path.Contains('*')) { internalKeys.RemoveAt(pathIndex); }
                                break;
                            }
                        }

                        if (!keep) result[i] = null;
                    }


                    result = result.Where(lin => !string.IsNullOrEmpty(lin)).ToArray();


                    string csvLine = string.Join('|', result.Select(c => c.Split(" : ").Last().Replace("\",", "\"").Replace("\"", string.Empty)).ToArray());

                    response.AppendLine(csvLine);

                }
                else
                {
                    string csvLine = string.Join('|', result.Select(c => c.Split(':').Last()).ToArray());

                    response.AppendLine(csvLine);

                }
            }
            json = response.ToString();

            return json;
        }

        /// <summary>
        /// Converts a list of any entity to a JSON string with all properties at the first level, matching path if any.
        /// </summary>
        /// <param name="entities">List of entities to be converted to JSON.</param>
        /// <param name="pathFields">Optional dictionary that specifies the path and fields to be included in the JSON string. If not provided, all properties are included.</param>
        /// <returns>A JSON string representation of the entities with properties at the first level, matching path if any.</returns>
        public static async Task<string> GetReportJson(object entities, Dictionary<string, string> pathFields = null)
        {

            //avoid selfReferenceLoop error
            JsonSerializerSettings settings = new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

            string jsonResponse = JsonConvert.SerializeObject(entities, settings);

            //Initialice JsonString and JArray
            var jsonArray = JArray.Parse(jsonResponse);

            var json = "[";

            foreach (var line in jsonArray)
            {

                //Cast line to JObject to be able to use Descendants()
                var internalEntity = (JObject)line;

                var internalKeys = pathFields.Keys.ToList();
                //Get from last one to first and construct its path as jsonkey and value as json value
                string[]? result = internalEntity.Descendants()
                  .Where(t => !t.HasValues)
                  .Select(t => "\"" + t.Path.WithoutPrefix() + "\"" + " : " + "\"" + t.ToString() + "\",")
                  .ToArray();

                //cut off the last ","
                result[result.Length - 1] = result[result.Length - 1]
                                          .Substring(0, result[result.Length - 1].Length - 1);
                if (pathFields != null)
                {
                    //Only selected fields
                    for (int i = 0; i <= result.Length - 1; i++)
                    {
                        bool keep = false;

                        for (int pathIndex = 0; pathIndex <= internalKeys.Count - 1; pathIndex++)
                        {
                            string path = internalKeys[pathIndex];

                            string resultPath = result[i].Unformatted().Trim().Replace("\"", string.Empty);

                            if (resultPath.HasPath(path))
                            {
                                keep = true;
                                //remove encountered field from list to avoid research on same entity
                                //Only if not list
                                if (!path.Contains('*')) { internalKeys.RemoveAt(pathIndex); }
                                break;
                            }
                        }

                        if (!keep) result[i] = null;
                    }
                    result = result.Where(lin => !string.IsNullOrEmpty(lin)).ToArray();

                    json += "{\n" + string.Join("\n", result) + "\n},";
                }
                else
                {
                    json += "{\n" + string.Join("\n", result) + "\n},";
                }
            }

            //cut off the last ","
            json = json.Substring(0, json.Length - 1) + "]";

            //At this point a valid all to 1st level json is available

            return json;
        }

    }
}

