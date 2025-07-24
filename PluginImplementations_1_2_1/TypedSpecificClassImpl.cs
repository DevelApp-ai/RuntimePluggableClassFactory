using DevelApp.RuntimePluggableClassFactory.Interface;
using DevelApp.Utility.Model;
using PluginImplementations;
using System;

namespace PluginImplementations
{
    /// <summary>
    /// Typed implementation demonstrating strongly-typed plugin execution
    /// </summary>
    public class TypedSpecificClassImpl : ITypedSpecificInterface
    {
        public IdentifierString Name
        {
            get
            {
                return GetType().Name;
            }
        }

        public string Description
        {
            get
            {
                return "A typed test class for plugins demonstrating type-safe execution";
            }
        }

        public SemanticVersionNumber Version
        {
            get
            {
                return GetType().Assembly.GetName().Version;
            }
        }

        public NamespaceString Module
        {
            get
            {
                return "Test";
            }
        }

        public PluginExecutionResult<WordGuessOutput> Execute(IPluginExecutionContext context, WordGuessInput input)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                context.Logger.LogInformation($"Processing word guess for: {input.Word}");
                
                if (string.IsNullOrEmpty(input.Word))
                {
                    return PluginExecutionResult<WordGuessOutput>.CreateFailure("Input word cannot be null or empty");
                }

                // Check if cancellation was requested
                context.CancellationToken.ThrowIfCancellationRequested();

                // Simulate some processing time
                System.Threading.Thread.Sleep(10);

                string targetWord = "Monster";
                bool isCorrect;
                
                if (input.CaseSensitive)
                {
                    isCorrect = input.Word.Equals(targetWord);
                }
                else
                {
                    isCorrect = input.Word.Equals(targetWord, StringComparison.OrdinalIgnoreCase);
                }

                var processingDuration = DateTime.UtcNow - startTime;
                
                var output = new WordGuessOutput
                {
                    IsCorrect = isCorrect,
                    ConfidenceScore = isCorrect ? 1.0 : 0.0,
                    Message = isCorrect ? "Correct guess!" : $"Incorrect. Expected '{targetWord}'",
                    ProcessedTime = DateTime.UtcNow,
                    ProcessingDuration = processingDuration
                };

                context.Logger.LogInformation($"Word guess result: {output.IsCorrect} (confidence: {output.ConfidenceScore})");
                
                return PluginExecutionResult<WordGuessOutput>.CreateSuccess(output);
            }
            catch (OperationCanceledException)
            {
                context.Logger.LogWarning("Plugin execution was cancelled");
                return PluginExecutionResult<WordGuessOutput>.CreateFailure("Execution was cancelled");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Plugin execution failed: {ex.Message}", ex);
                return PluginExecutionResult<WordGuessOutput>.CreateFailure($"Execution failed: {ex.Message}", ex);
            }
        }
    }
}

