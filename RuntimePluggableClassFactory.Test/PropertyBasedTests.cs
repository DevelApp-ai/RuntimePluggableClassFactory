using DevelApp.RuntimePluggableClassFactory.BulkOperations;
using DevelApp.RuntimePluggableClassFactory.Resilience;
using DevelApp.RuntimePluggableClassFactory.SemanticVersioning;
using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace DevelApp.RuntimePluggableClassFactory.Test
{
    /// <summary>
    /// Property-based tests using xUnit's Theory with InlineData
    /// Tests invariants and properties of the system
    /// </summary>
    public class PropertyBasedTests
    {
        // ============================================================================
        // Version Range Property Tests
        // ============================================================================

        [Theory]
        [InlineData("1.0.0", "1.0.0", true)]
        [InlineData("1.0.0", "1.0.1", false)]
        [InlineData("1.0.0", "0.9.9", false)]
        [InlineData("1.0.0-2.0.0", "1.5.0", true)]
        [InlineData("1.0.0-2.0.0", "1.0.0", true)]
        [InlineData("1.0.0-2.0.0", "2.0.0", true)]
        [InlineData("1.0.0-2.0.0", "0.9.9", false)]
        [InlineData("1.0.0-2.0.0", "2.0.1", false)]
        public void VersionRange_Contains_ReturnsExpected(string rangeString, string versionString, bool expected)
        {
            // Arrange
            var range = VersionRange.Parse(rangeString);
            var version = SemanticVersionNumber.Parse(versionString);

            // Act
            var result = range.Contains(version);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1.0.0", "1.0.0")]
        [InlineData("1.0.0-2.0.0", "1.0.0-2.0.0")]
        [InlineData(">=1.0.0", ">=1.0.0")]
        [InlineData("<=2.0.0", "<=2.0.0")]
        public void VersionRange_Equals_ReturnsTrueForSameRange(string rangeString1, string rangeString2)
        {
            // Arrange
            var range1 = VersionRange.Parse(rangeString1);
            var range2 = VersionRange.Parse(rangeString2);

            // Act & Assert
            Assert.Equal(range1, range2);
        }

        [Theory]
        [InlineData("1.0.0", ">=1.0.0")]
        [InlineData("1.0.0-2.0.0", ">=1.0.0")]
        [InlineData("1.0.0", "2.0.0")]
        public void VersionRange_NotEquals_ReturnsFalseForDifferentRanges(string rangeString1, string rangeString2)
        {
            // Arrange
            var range1 = VersionRange.Parse(rangeString1);
            var range2 = VersionRange.Parse(rangeString2);

            // Act & Assert
            Assert.NotEqual(range1, range2);
        }

        [Theory]
        [InlineData("1.0.0", true)]
        [InlineData("1.0.0-1.0.0", true)]
        [InlineData("1.0.0-2.0.0", false)]
        [InlineData(">=1.0.0", false)]
        public void VersionRange_IsSpecificVersion_ReturnsExpected(string rangeString, bool expected)
        {
            // Arrange
            var range = VersionRange.Parse(rangeString);

            // Act & Assert
            Assert.Equal(expected, range.IsSpecificVersion);
        }

        [Theory]
        [InlineData("1.0.0", "2.0.0", false)]
        [InlineData("1.0.0-2.0.0", "1.5.0-1.7.0", true)]
        [InlineData("1.0.0-2.0.0", "2.0.0-3.0.0", true)]
        [InlineData("1.0.0-2.0.0", "3.0.0-4.0.0", false)]
        public void VersionRange_Intersects_ReturnsExpected(string rangeString1, string rangeString2, bool expected)
        {
            // Arrange
            var range1 = VersionRange.Parse(rangeString1);
            var range2 = VersionRange.Parse(rangeString2);

            // Act
            var result = range1.Intersects(range2);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("1.0.0-3.0.0", "1.5.0-2.5.0", true)]
        [InlineData("1.0.0-3.0.0", "0.5.0-1.5.0", false)]
        public void VersionRange_Contains_Range_ReturnsExpected(string rangeString1, string rangeString2, bool expected)
        {
            // Arrange
            var range1 = VersionRange.Parse(rangeString1);
            var range2 = VersionRange.Parse(rangeString2);

            // Act
            var result = range1.Contains(range2);

            // Assert
            Assert.Equal(expected, result);
        }

        // ============================================================================
        // Circuit Breaker Property Tests
        // ============================================================================

        [Theory]
        [InlineData(1, true)]
        [InlineData(5, true)]
        [InlineData(10, true)]
        public void CircuitBreaker_InitialState_IsClosed(int failureThreshold)
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker(failureThreshold);

            // Act & Assert
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
            Assert.True(circuitBreaker.AllowsExecution);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void CircuitBreaker_AfterFailureThreshold_Opens(int failureThreshold)
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker(failureThreshold, TimeSpan.FromSeconds(1));

            // Act - Record enough failures to open the circuit
            for (int i = 0; i < failureThreshold; i++)
            {
                try
                {
                    circuitBreaker.Execute(() => throw new Exception("Test failure"));
                }
                catch { }
            }

            // Assert
            Assert.Equal(CircuitState.Open, circuitBreaker.State);
            Assert.False(circuitBreaker.AllowsExecution);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void CircuitBreaker_AfterReset_Closes(int failureThreshold)
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker(failureThreshold, TimeSpan.FromSeconds(1));

            // Act - Open the circuit
            for (int i = 0; i < failureThreshold; i++)
            {
                try
                {
                    circuitBreaker.Execute(() => throw new Exception("Test failure"));
                }
                catch { }
            }

            // Reset
            circuitBreaker.ForceReset();

            // Assert
            Assert.Equal(CircuitState.Closed, circuitBreaker.State);
            Assert.True(circuitBreaker.AllowsExecution);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CircuitBreaker_WithFallback_ReturnsFallbackWhenOpen(bool useFallback)
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker(1, TimeSpan.FromSeconds(10));
            var fallbackCalled = false;

            // Open the circuit
            try
            {
                circuitBreaker.Execute(() => throw new Exception("Test failure"));
            }
            catch { }

            // Act
            if (useFallback)
            {
                var result = circuitBreaker.Execute(() => "success", () => { fallbackCalled = true; return "fallback"; });
                Assert.Equal("fallback", result);
                Assert.True(fallbackCalled);
            }
            else
            {
                Assert.Throws<CircuitOpenException>(() => 
                    circuitBreaker.Execute(() => "success"));
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(5, 5)]
        public void CircuitBreaker_FailureCount_IncrementsOnFailure(int failureThreshold, int expectedFailures)
        {
            // Arrange
            var circuitBreaker = new CircuitBreaker(failureThreshold);

            // Act - Record failures
            for (int i = 0; i < expectedFailures; i++)
            {
                try
                {
                    circuitBreaker.Execute(() => throw new Exception("Test failure"));
                }
                catch { }
            }

            // Assert
            Assert.Equal(expectedFailures, circuitBreaker.FailureCount);
        }

        // ============================================================================
        // Bulk Operations Property Tests
        // ============================================================================

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public void BulkLoadResult_EmptyList_HasZeroAttempts(int expectedCount)
        {
            // Arrange
            var result = new PluginBulkOperations.BulkLoadResult<TestPlugin>();

            // Act & Assert
            Assert.Equal(0, result.TotalAttempted);
            Assert.Equal(0, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            Assert.True(result.AllSucceeded);
        }

        [Theory]
        [InlineData(1, 0, 1)]
        [InlineData(2, 0, 2)]
        [InlineData(0, 1, 1)]
        [InlineData(3, 2, 5)]
        public void BulkLoadResult_TotalAttempted_IsSumOfSuccessAndFailure(int successCount, int failureCount, int expectedTotal)
        {
            // Arrange
            var result = new PluginBulkOperations.BulkLoadResult<TestPlugin>();

            // Add successes
            for (int i = 0; i < successCount; i++)
            {
                result.AddSuccess(new NamespaceString("Test"), new IdentifierString("Plugin" + i), new TestPlugin());
            }

            // Add failures
            for (int i = 0; i < failureCount; i++)
            {
                result.AddFailure(new NamespaceString("Test"), new IdentifierString("Plugin" + i), 
                    new PluginBulkOperations.PluginLoadError
                    {
                        Code = Errors.PluginErrorCode.Instantiation_Failed,
                        Message = "Test failure"
                    });
            }

            // Act & Assert
            Assert.Equal(expectedTotal, result.TotalAttempted);
            Assert.Equal(successCount, result.SuccessCount);
            Assert.Equal(failureCount, result.FailureCount);
            Assert.Equal(failureCount == 0, result.AllSucceeded);
        }

        // ============================================================================
        // Async Plugin Loader Property Tests
        // ============================================================================

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AsyncPluginLoader_InitialLoadTask_Completes(bool useAsync)
        {
            // Arrange
            var tempPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(tempPath);

            try
            {
                var innerLoader = new FilePlugin.FilePluginLoader<TestPlugin>(
                    new Uri(tempPath));
                var asyncLoader = new Async.AsyncPluginLoader<TestPlugin>(innerLoader);

                // Act
                var initialLoadTask = asyncLoader.InitialLoadTask;

                // Assert - Task should complete (either successfully or with exception)
                // We don't care about the result, just that it completes
                var completed = await Task.WhenAny(
                    initialLoadTask,
                    Task.Delay(5000)).ConfigureAwait(false);

                Assert.Same(initialLoadTask, completed);
            }
            finally
            {
                System.IO.Directory.Delete(tempPath, true);
            }
        }

        // ============================================================================
        // Helper Classes
        // ============================================================================

        /// <summary>
        /// Test plugin implementation for property tests
        /// </summary>
        private class TestPlugin : Interface.IPluginClass
        {
            public NamespaceString Module => new NamespaceString("Test");
            public IdentifierString Name => new IdentifierString("TestPlugin");
            public SemanticVersionNumber Version => SemanticVersionNumber.Parse("1.0.0");
            public string? Description => "Test plugin for property-based tests";
        }
    }
}
