using ModelStoreApi.Domain;
using System.Text.Json;

namespace ModelStoreApi.Dtos
{
    public record ModelDto(DateTime DateTime, string Id, string Class, string Module, string Args, string KWArgs, string Tag, int TrainableParams, double? MinValLoss, double? MaxValAccuracy, ModelStatus Status)
    {
        public ModelDto(Model model) 
            : this(model.DateTime, 
                  model.Id, 
                  model.Class, 
                  model.Module, 
                  JsonSerializer.Serialize(model.Args), 
                  JsonSerializer.Serialize(model.KWArgs), 
                  model.Tag,
                  model.TrainableParams, 
                  model.Metrics?.MinValLoss,
                  model.Metrics?.MaxValAccuracy,
                  model.Status)
        {
        }
    }
}
