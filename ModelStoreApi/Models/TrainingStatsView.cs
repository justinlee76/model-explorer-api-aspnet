using System.Text.Json;

namespace ModelStoreApi.Models
{
    public record TrainingStatsView(DateTime DateTime, string Id, string Class, string Module, string Args, string KWArgs, string Tag, int TrainableParams, double MinValLoss, double MaxValAccuracy)
    {
        public TrainingStatsView(TrainingStats ts) 
            : this(ts.DateTime, 
                  ts.Id.ToString(), 
                  ts.Class, 
                  ts.Module, 
                  JsonSerializer.Serialize(ts.Args), 
                  JsonSerializer.Serialize(ts.KWArgs), 
                  ts.Tag, ts.TrainableParams, 
                  ts.MinValLoss, 
                  ts.MaxValAccuracy)
        {

        }
    }
}
