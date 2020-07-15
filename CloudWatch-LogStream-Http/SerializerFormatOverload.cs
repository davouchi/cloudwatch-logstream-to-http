using Newtonsoft.Json;
using System;

namespace CloudWatch_LogStream_Http
{
    public class SerializerFormatOverload : JsonConverter
    {
        private readonly string oldValue;
        private readonly string newValue;

        public SerializerFormatOverload(string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(oldValue))
                throw new ArgumentException("string.IsNullOrEmpty(oldValue)");
            if (newValue == null)
                throw new ArgumentNullException("newValue");
            this.oldValue = oldValue;
            this.newValue = newValue;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(string);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead { get { return false; } }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var s = ((string)value).Replace(oldValue, newValue);
            writer.WriteValue(s);
        }
    }
}