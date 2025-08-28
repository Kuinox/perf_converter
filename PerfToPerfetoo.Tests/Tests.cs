using PerfettoProcessor;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PerfToPerfetoo.Tests
{
    public class Tests
    {
        static readonly string _exePath = Path.Combine(AppContext.BaseDirectory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "trace_processor_shell.exe" : "trace_processor_shell");
        static readonly PerfettoTraceProcessor _processor = new();


        [Test]
        public void Test1()
        {
            var errors = new List<string>();
            _processor.OpenTraceProcessor(_exePath, @"C:\dev\PerfConverter\PerfToPerfetto\bin\Debug\net9.0\out.ftf",
            (ctx, data) =>
            {
                Console.WriteLine(data.Data);
            }, (ctx, data) =>
            {
                if(string.IsNullOrEmpty(data.Data))
                    return;
                if (data.Data.Contains("Loading trace:")
                || data.Data.Contains("Trace loaded:")
                || data.Data.Contains("[HTTP]"))
                    return;

                errors.Add(data.Data!);
                Assert.Fail(data.Data!);
            });

            var query = new PerfettoTrackEvent();


            // track_event_tokenizer_errors
            _processor.QueryTraceForEvents(query.GetSqlQuery(), query.GetEventKey(), (reader) =>
            {
            });

            Assert.That(errors.Count, Is.EqualTo(0), string.Join(Environment.NewLine, errors));
        }
    }
}
