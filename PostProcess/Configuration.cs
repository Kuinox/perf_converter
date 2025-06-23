using IronCompress;
using Parquet;
using System;

namespace PostProcess
{
    internal static class Configuration
    {
        static bool _batchSizeInit;
        static int _batchSize;
        public static int BatchSize
        {
            get
            {
                if (_batchSizeInit) return _batchSize;
                _batchSizeInit = true;
                const int defaultBatchSize = 2_000_000;
                string? batchEnv = Environment.GetEnvironmentVariable("BATCH_SIZE");
                if (!string.IsNullOrEmpty(batchEnv) && int.TryParse(batchEnv, out var parsedBatch))
                {
                    _batchSize = parsedBatch;
                }
                else
                {
                    _batchSize = defaultBatchSize;
                }
                return _batchSize;
            }
        }

        static bool _init;
        public static CompressionMethod CompressionMethod
        {
            get
            {
                if (_init) return field;
                _init = true;
                field = CompressionMethod.Snappy;
                string? compressEnv = Environment.GetEnvironmentVariable("PARQUET_COMPRESSION");
                if (!string.IsNullOrEmpty(compressEnv) && Enum.TryParse<CompressionMethod>(compressEnv, true, out var parsedCompress))
                    field = parsedCompress;
                return field;
            }
        }
    }
}
