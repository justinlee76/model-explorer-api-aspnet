using ModelStoreApi.Domain;

namespace ModelStoreApi.Dtos
{
    public sealed record ModelData
    {
        public DateTime DateTime { get; init; }

        public string Id { get; init; } = string.Empty;

        public string Module { get; init; } = string.Empty;

        public string ClassName { get; init; } = string.Empty;

        public object[] Args { get; init; } = [];

        public Dictionary<string, object> Kwargs { get; init; } = [];

        public string Tag { get; init; } = string.Empty;

        public int TrainableParams { get; init; }

        public double? MinValLoss { get; init; }

        public double? MaxValAccuracy { get; init; }

        public string Status { get; init; } = string.Empty;

        public static ModelData FromDomain(Model model)
        {
            return new ModelData
            {
                DateTime = model.DateTime,
                Id = model.Id,
                Module = model.Module,
                ClassName = model.Class,
                Args = model.Args ?? [],
                Kwargs = model.KWArgs ?? [],
                Tag = model.Tag,
                TrainableParams = model.TrainableParams,
                MinValLoss = model.Metrics?.MinValLoss,
                MaxValAccuracy = model.Metrics?.MaxValAccuracy,
                Status = model.Status.ToString()
            };
        }
    }
}
