using DevelApp.RuntimePluggableClassFactory.Interface;
using System;

namespace PluginImplementations
{
    /// <summary>
    /// Typed version of the specific interface demonstrating type-safe plugin execution
    /// Input: WordGuessInput with the word to check
    /// Output: WordGuessOutput with the result and confidence score
    /// </summary>
    public interface ITypedSpecificInterface : ITypedPluginClass<WordGuessInput, WordGuessOutput>
    {
    }

    /// <summary>
    /// Strongly-typed input data for word guessing
    /// </summary>
    public class WordGuessInput
    {
        public string Word { get; set; }
        public bool CaseSensitive { get; set; } = false;
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Strongly-typed output data for word guessing results
    /// </summary>
    public class WordGuessOutput
    {
        public bool IsCorrect { get; set; }
        public double ConfidenceScore { get; set; }
        public string Message { get; set; }
        public DateTime ProcessedTime { get; set; } = DateTime.UtcNow;
        public TimeSpan ProcessingDuration { get; set; }
    }
}

