using Temp.Schema;

namespace PerfConverter;

/// <summary>
/// Manages trace segmentation based on aux data loss events
/// </summary>
public class TraceSegmentManager
{
    private readonly Dictionary<ulong, List<ulong>> _auxDataLossByTid = new();
    private readonly Dictionary<ulong, int> _currentSegmentByTid = new();
    private readonly Dictionary<ulong, Stack<ulong>> _remainingLossTimesByTid = new();

    public TraceSegmentManager()
    {
        LoadAuxDataLoss();
    }

    private void LoadAuxDataLoss()
    {
        var auxDataLossJson = Environment.GetEnvironmentVariable("AUX_DATA_LOSS");
        if (string.IsNullOrEmpty(auxDataLossJson))
        {
            Console.Error.WriteLine("No AUX_DATA_LOSS environment variable found - traces will not be segmented");
            return;
        }

        try
        {
            var auxDataLossEvents = ParseAuxDataLossJson(auxDataLossJson);
            if (auxDataLossEvents == null || auxDataLossEvents.Length == 0)
            {
                Console.Error.WriteLine("No aux data loss events found - traces will not be segmented");
                return;
            }

            // Group aux data loss events by TID and sort by time
            foreach (var auxEvent in auxDataLossEvents)
            {
                if (!_auxDataLossByTid.TryGetValue(auxEvent.Tid, out var lossTimes))
                {
                    lossTimes = new List<ulong>();
                    _auxDataLossByTid[auxEvent.Tid] = lossTimes;
                }
                lossTimes.Add(auxEvent.Time);
            }

            // Sort loss times and initialize current segments
            foreach (var (tid, lossTimes) in _auxDataLossByTid)
            {
                lossTimes.Sort();
                _currentSegmentByTid[tid] = 0;
                _remainingLossTimesByTid[tid] = new Stack<ulong>(lossTimes.AsEnumerable().Reverse());
            }

            Console.Error.WriteLine($"Loaded aux data loss events for {_auxDataLossByTid.Count} TIDs");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error parsing AUX_DATA_LOSS: {ex.Message} - traces will not be segmented");
        }
    }

    private static AuxDataLost[]? ParseAuxDataLossJson(string json)
    {
        // Simple manual parsing to avoid AOT issues with System.Text.Json
        // Expected format: [{"Time":123,"Tid":456,"Pid":789}, ...]
        
        json = json.Trim();
        if (!json.StartsWith('[') || !json.EndsWith(']'))
            return null;
            
        json = json[1..^1]; // Remove outer brackets
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<AuxDataLost>();
            
        var objects = json.Split("},{", StringSplitOptions.RemoveEmptyEntries);
        var results = new List<AuxDataLost>();
        
        foreach (var obj in objects)
        {
            var cleanObj = obj.Trim('{', '}');
            var props = cleanObj.Split(',');
            
            ulong time = 0, tid = 0, pid = 0;
            
            foreach (var prop in props)
            {
                var parts = prop.Split(':');
                if (parts.Length != 2) continue;
                
                var key = parts[0].Trim().Trim('"');
                var value = parts[1].Trim();
                
                if (key == "Time" && ulong.TryParse(value, out var timeVal))
                    time = timeVal;
                else if (key == "Tid" && ulong.TryParse(value, out var tidVal))
                    tid = tidVal;
                else if (key == "Pid" && ulong.TryParse(value, out var pidVal))
                    pid = pidVal;
            }
            
            if (time > 0 && tid > 0 && pid > 0)
                results.Add(new AuxDataLost(time, tid, pid));
        }
        
        return results.ToArray();
    }

    /// <summary>
    /// Gets the current segment ID for a given TID and timestamp
    /// </summary>
    public int GetSegmentId(ulong tid, ulong timestamp)
    {
        // If no aux data loss events for this TID, use segment 0
        if (!_auxDataLossByTid.ContainsKey(tid))
        {
            return 0;
        }

        // Check if we need to advance to the next segment
        if (_remainingLossTimesByTid.TryGetValue(tid, out var remainingLossTimes) && 
            remainingLossTimes.Count > 0 && 
            timestamp >= remainingLossTimes.Peek())
        {
            // We've crossed a data loss boundary - advance to next segment
            remainingLossTimes.Pop();
            _currentSegmentByTid[tid]++;
            Console.Error.WriteLine($"TID {tid}: Advanced to segment {_currentSegmentByTid[tid]} at timestamp {timestamp}");
        }

        return _currentSegmentByTid[tid];
    }

    /// <summary>
    /// Generates a key for persistence that includes segment information
    /// </summary>
    public string GenerateSegmentKey(int pid, ulong tid, ulong timestamp)
    {
        var segmentId = GetSegmentId(tid, timestamp);
        return $"{pid}/{tid}/segment_{segmentId}";
    }
}