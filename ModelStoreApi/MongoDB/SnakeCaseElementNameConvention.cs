using System.Text;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;

namespace ModelStoreApi.MongoDB
{
    public sealed class SnakeCaseElementNameConvention : ConventionBase, IMemberMapConvention
    {
        public void Apply(BsonMemberMap memberMap)
        {
            memberMap.SetElementName(GetElementName(memberMap.MemberName));
        }

        private static string GetElementName(string memberName)
        {
            return memberName switch
            {
                "Id" => "_id",
                "DateTime" => "datetime",
                "KWArgs" => "kwargs",
                _ => ToSnakeCase(memberName)
            };
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (char.IsUpper(current))
                {
                    var hasPrevious = i > 0;
                    var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                    var previousIsLowerOrDigit = hasPrevious && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));
                    var previousIsUpper = hasPrevious && char.IsUpper(value[i - 1]);

                    if (hasPrevious && (previousIsLowerOrDigit || previousIsUpper && nextIsLower))
                        builder.Append('_');

                    builder.Append(char.ToLowerInvariant(current));
                }
                else
                {
                    builder.Append(current);
                }
            }

            return builder.ToString();
        }
    }
}
