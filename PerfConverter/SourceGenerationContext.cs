using System.Text.Json.Serialization;
using Temp.Schema;

namespace PerfConverter;

[JsonSerializable(typeof(AuxDataLost[]))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
