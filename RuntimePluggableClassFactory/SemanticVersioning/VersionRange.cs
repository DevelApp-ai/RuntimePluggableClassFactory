using DevelApp.Utility.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DevelApp.RuntimePluggableClassFactory.SemanticVersioning
{
    /// <summary>
    /// Represents a range of semantic versions
    /// Supports various range formats: exact, >=, <=, >, <, between, etc.
    /// </summary>
    public class VersionRange : IEquatable<VersionRange>
    {
        /// <summary>
        /// Minimum version (inclusive)
        /// </summary>
        public SemanticVersionNumber? MinVersion { get; }

        /// <summary>
        /// Maximum version (inclusive)
        /// </summary>
        public SemanticVersionNumber? MaxVersion { get; }

        /// <summary>
        /// Whether min version is inclusive
        /// </summary>
        public bool MinInclusive { get; } = true;

        /// <summary>
        /// Whether max version is inclusive
        /// </summary>
        public bool MaxInclusive { get; } = true;

        /// <summary>
        /// Creates a version range
        /// </summary>
        /// <param name="minVersion">Minimum version</param>
        /// <param name="maxVersion">Maximum version</param>
        /// <param name="minInclusive">Whether min is inclusive</param>
        /// <param name="maxInclusive">Whether max is inclusive</param>
        public VersionRange(
            SemanticVersionNumber? minVersion = null,
            SemanticVersionNumber? maxVersion = null,
            bool minInclusive = true,
            bool maxInclusive = true)
        {
            if (minVersion != null && maxVersion != null && minVersion > maxVersion)
            {
                throw new ArgumentException("Min version cannot be greater than max version");
            }

            MinVersion = minVersion;
            MaxVersion = maxVersion;
            MinInclusive = minInclusive;
            MaxInclusive = maxInclusive;
        }

        /// <summary>
        /// Creates a version range from a string
        /// </summary>
        /// <param name="rangeString">Range string (e.g., "1.0.0", ">=1.0.0", "1.0.0-2.0.0")</param>
        /// <returns>Version range</returns>
        public static VersionRange Parse(string rangeString)
        {
            if (string.IsNullOrWhiteSpace(rangeString))
            {
                throw new ArgumentException("Range string cannot be null or empty", nameof(rangeString));
            }

            rangeString = rangeString.Trim();

            // Exact version
            if (rangeString.StartsWith("=") || !rangeString.Contains("-") && !rangeString.Contains(">") && !rangeString.Contains("<"))
            {
                var version = SemanticVersionNumber.Parse(rangeString.TrimStart('='));
                return new VersionRange(version, version);
            }

            // Greater than or equal
            if (rangeString.StartsWith(">="))
            {
                var version = SemanticVersionNumber.Parse(rangeString.Substring(2));
                return new VersionRange(version, null, true, true);
            }

            // Greater than
            if (rangeString.StartsWith(">"))
            {
                var version = SemanticVersionNumber.Parse(rangeString.Substring(1));
                return new VersionRange(version, null, false, true);
            }

            // Less than or equal
            if (rangeString.StartsWith("<="))
            {
                var version = SemanticVersionNumber.Parse(rangeString.Substring(2));
                return new VersionRange(null, version, true, true);
            }

            // Less than
            if (rangeString.StartsWith("<"))
            {
                var version = SemanticVersionNumber.Parse(rangeString.Substring(1));
                return new VersionRange(null, version, true, false);
            }

            // Range (e.g., "1.0.0-2.0.0")
            if (rangeString.Contains("-"))
            {
                var parts = rangeString.Split('-');
                if (parts.Length != 2)
                {
                    throw new FormatException("Invalid range format. Use 'min-max' or prefix operators");
                }

                var minVersion = SemanticVersionNumber.Parse(parts[0]);
                var maxVersion = SemanticVersionNumber.Parse(parts[1]);
                return new VersionRange(minVersion, maxVersion);
            }

            throw new FormatException("Invalid version range format");
        }

        /// <summary>
        /// Checks if a version is within this range
        /// </summary>
        /// <param name="version">Version to check</param>
        /// <returns>True if version is in range</returns>
        public bool Contains(SemanticVersionNumber version)
        {
            if (version == null)
                return false;

            // Check min
            if (MinVersion != null)
            {
                if (MinInclusive)
                {
                    if (version < MinVersion)
                        return false;
                }
                else
                {
                    if (version <= MinVersion)
                        return false;
                }
            }

            // Check max
            if (MaxVersion != null)
            {
                if (MaxInclusive)
                {
                    if (version > MaxVersion)
                        return false;
                }
                else
                {
                    if (version >= MaxVersion)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if this range intersects with another range
        /// </summary>
        /// <param name="other">Other range</param>
        /// <returns>True if ranges intersect</returns>
        public bool Intersects(VersionRange other)
        {
            if (other == null)
                return false;

            // If either range is unbounded, they intersect
            if ((MinVersion == null || MaxVersion == null) && 
                (other.MinVersion == null || other.MaxVersion == null))
            {
                return true;
            }

            // Check if ranges overlap
            var thisMin = MinVersion ?? SemanticVersionNumber.MinValue;
            var thisMax = MaxVersion ?? SemanticVersionNumber.MaxValue;
            var otherMin = other.MinVersion ?? SemanticVersionNumber.MinValue;
            var otherMax = other.MaxVersion ?? SemanticVersionNumber.MaxValue;

            return thisMin <= otherMax && otherMin <= thisMax;
        }

        /// <summary>
        /// Gets the intersection of two ranges
        /// </summary>
        /// <param name="other">Other range</param>
        /// <returns>Intersection range or null if no intersection</returns>
        public VersionRange? Intersect(VersionRange other)
        {
            if (!Intersects(other))
                return null;

            var minVersion = Max(MinVersion, other.MinVersion);
            var maxVersion = Min(MaxVersion, other.MaxVersion);
            
            return new VersionRange(minVersion, maxVersion);
        }

        /// <summary>
        /// Checks if this range contains another range
        /// </summary>
        /// <param name="other">Other range</param>
        /// <returns>True if this range contains the other range</returns>
        public bool Contains(VersionRange other)
        {
            if (other == null)
                return false;

            if (MinVersion == null && MaxVersion == null)
                return true;

            if (MinVersion == null)
                return (MaxVersion == null || other.MaxVersion == null || other.MaxVersion <= MaxVersion);

            if (MaxVersion == null)
                return (MinVersion == null || other.MinVersion == null || other.MinVersion >= MinVersion);

            return (MinVersion <= other.MinVersion && MaxVersion >= other.MaxVersion);
        }

        /// <summary>
        /// Filters a list of versions to those within this range
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <returns>Filtered list</returns>
        public IEnumerable<SemanticVersionNumber> Filter(IEnumerable<SemanticVersionNumber> versions)
        {
            return versions.Where(v => Contains(v));
        }

        /// <summary>
        /// Gets the newest version within this range
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <returns>Newest version in range or null</returns>
        public SemanticVersionNumber? GetNewest(IEnumerable<SemanticVersionNumber> versions)
        {
            return Filter(versions).OrderByDescending(v => v).FirstOrDefault();
        }

        /// <summary>
        /// Gets the oldest version within this range
        /// </summary>
        /// <param name="versions">List of versions</param>
        /// <returns>Oldest version in range or null</returns>
        public SemanticVersionNumber? GetOldest(IEnumerable<SemanticVersionNumber> versions)
        {
            return Filter(versions).OrderBy(v => v).FirstOrDefault();
        }

        /// <summary>
        /// Checks if this range is for a specific version
        /// </summary>
        public bool IsSpecificVersion => MinVersion != null && MaxVersion != null && MinVersion == MaxVersion;

        /// <summary>
        /// Gets the specific version if this is a specific version range
        /// </summary>
        public SemanticVersionNumber? SpecificVersion => IsSpecificVersion ? MinVersion : null;

        public override string ToString()
        {
            if (IsSpecificVersion)
                return MinVersion?.ToString() ?? "*";

            var parts = new List<string>();
            
            if (MinVersion != null)
            {
                parts.Add((MinInclusive ? ">=" : ">") + MinVersion.ToString());
            }
            
            if (MaxVersion != null)
            {
                parts.Add((MaxInclusive ? "<=" : "<") + MaxVersion.ToString());
            }
            
            if (parts.Count == 0)
                return "*";
            
            if (parts.Count == 1)
                return parts[0];
            
            return parts[0] + " & " + parts[1];
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as VersionRange);
        }

        public bool Equals(VersionRange? other)
        {
            if (other == null)
                return false;

            return MinVersion == other.MinVersion &&
                   MaxVersion == other.MaxVersion &&
                   MinInclusive == other.MinInclusive &&
                   MaxInclusive == other.MaxInclusive;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (MinVersion?.GetHashCode() ?? 0);
                hash = hash * 23 + (MaxVersion?.GetHashCode() ?? 0);
                hash = hash * 23 + MinInclusive.GetHashCode();
                hash = hash * 23 + MaxInclusive.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(VersionRange? left, VersionRange? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;
            return left.Equals(right);
        }

        public static bool operator !=(VersionRange? left, VersionRange? right)
        {
            return !(left == right);
        }

        private static SemanticVersionNumber? Max(SemanticVersionNumber? a, SemanticVersionNumber? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a > b ? a : b;
        }

        private static SemanticVersionNumber? Min(SemanticVersionNumber? a, SemanticVersionNumber? b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a < b ? a : b;
        }
    }
}
