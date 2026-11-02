using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DevelApp.RuntimePluggableClassFactory.Containerized.SBoB
{
    /// <summary>
    /// Thrown when an SBoB profile is not in the canonical emission form
    /// (SBOB-Forge TDS §8.1). Verification is fail-closed: a file that is not
    /// canonical output could hide behavior and is never silently repaired.
    /// </summary>
    public sealed class SBoBCanonicalFormatException : Exception
    {
        /// <summary>One-based line number the violation was detected at (0 when the defect is not line-localized).</summary>
        public int LineNumber { get; }

        public SBoBCanonicalFormatException(string message, int lineNumber)
            : base(lineNumber > 0 ? $"line {lineNumber}: {message}" : message)
        {
            LineNumber = lineNumber;
        }
    }

    /// <summary>
    /// Strict, line-oriented parser for canonically emitted SBoB profiles.
    /// It accepts exactly the output grammar of SBOB-Forge's CanonicalSbobEmitter
    /// (TDS §8.1) in either dialect header (TDS §10.4) and rejects everything else:
    /// CRLF, tabs, comments, document markers, blank lines, trailing whitespace,
    /// unknown keys or sections, out-of-order sections/fields, single-quoted or
    /// non-canonically quoted scalars, "..." wildcards, and non-UTF-8 bytes.
    /// Sections SBOB-Forge cannot emit in v1 (capabilities, endpoints, rulePolicies)
    /// are rejected as non-canonical for this verifier version — content this
    /// verifier cannot enforce must not pass silently.
    /// </summary>
    public static class SBoBCanonicalParser
    {
        public const string KubescapeApiVersion = "spdx.softwarecomposition.kubescape.io/v1beta1";
        public const string KubescapeKind = "ContainerProfile";
        public const string RegistryApiVersion = "sbob.k8sstormcenter.io/v1";
        public const string RegistryKind = "SoftwareBillOfBehavior";

        public const string ManagedByAnnotation = "kubescape.io/managed-by";
        public const string BindingAnnotation = "develapp.io/binding";
        public const string GeneratorAnnotation = "develapp.io/generator";
        public const string GrammarLockAnnotation = "develapp.io/grammar-lock-sha256";
        public const string BehaviorHashAnnotation = "develapp.io/behavior-hash";
        public const string SpecVersionAnnotation = "sbob.spec-version";

        /// <summary>The only binding this verifier understands: the profile is bound to a package purl.</summary>
        public const string PackagePurlBinding = "package-purl";

        /// <summary>Wildcard codepoint (U+22EF). The ASCII run "..." is never canonical.</summary>
        public const char SegmentWildcard = '\u22EF';

        private static readonly Regex PurlShape = new(
            @"^pkg:[a-z0-9][a-z0-9.+-]*/[^@\s]+@[^@\s]+$",
            RegexOptions.Compiled);

        // Canonical spec section order (TDS §8.1). capabilities/endpoints/rulePolicies are not emittable in v1.
        private static readonly string[] KnownSections = { "execs", "opens", "ingress", "egress" };

        /// <summary>Parse a canonically emitted SBoB profile. Throws <see cref="SBoBCanonicalFormatException"/> on any deviation.</summary>
        public static SBoBProfile Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                throw new SBoBCanonicalFormatException("profile is empty", 0);
            }

            var lines = PreValidate(text);
            int i = 0;

            var dialect = ParseHeader(lines, ref i);
            var (metadataName, metadataNamespace, annotations) = ParseMetadata(lines, ref i, dialect);
            ParseSpec(lines, ref i,
                out var imageId, out var architectures,
                out var execs, out var opens, out var ingress, out var egress);

            if (i != lines.Count)
            {
                throw new SBoBCanonicalFormatException(
                    $"unexpected content after the profile: '{lines[i].Content}'", lines[i].Number);
            }

            string apiVersion = dialect == SBoBDialect.KubescapeContainerProfile ? KubescapeApiVersion : RegistryApiVersion;
            string kind = dialect == SBoBDialect.KubescapeContainerProfile ? KubescapeKind : RegistryKind;

            return new SBoBProfile(
                dialect, apiVersion, kind, metadataName, metadataNamespace, annotations,
                architectures, imageId, execs, opens, ingress, egress);
        }

        private sealed class ProfileLine
        {
            public required string Content { get; init; }
            public required int Indent { get; init; }
            public required int Number { get; init; }
        }

        private static List<ProfileLine> PreValidate(string text)
        {
            if (text.Contains('\r'))
            {
                throw new SBoBCanonicalFormatException("carriage return found (canonical profiles use LF line endings only)", 0);
            }
            if (text.Contains('\t'))
            {
                throw new SBoBCanonicalFormatException("tab character found (canonical profiles use 2-space indentation)", 0);
            }
            if (text[^1] != '\n')
            {
                throw new SBoBCanonicalFormatException("profile must end with a newline", 0);
            }

            var result = new List<ProfileLine>();
            int number = 0;
            int start = 0;
            while (start < text.Length)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0)
                {
                    end = text.Length; // defensive: covered by the trailing-newline check
                }
                number++;
                var raw = text[start..end];
                start = end + 1;

                if (raw.Length == 0)
                {
                    throw new SBoBCanonicalFormatException("blank line found (canonical profiles contain no blank lines)", number);
                }
                if (char.IsWhiteSpace(raw[^1]))
                {
                    throw new SBoBCanonicalFormatException("trailing whitespace", number);
                }
                if (raw.TrimStart().StartsWith('#'))
                {
                    throw new SBoBCanonicalFormatException("comment found (canonical profiles contain no comments)", number);
                }
                var trimmed = raw.Trim();
                if (trimmed is "---" or "...")
                {
                    throw new SBoBCanonicalFormatException("YAML document marker found", number);
                }

                int indent = 0;
                while (indent < raw.Length && raw[indent] == ' ')
                {
                    indent++;
                }
                if (indent % 2 != 0)
                {
                    throw new SBoBCanonicalFormatException("odd indentation (canonical profiles use 2-space steps)", number);
                }

                result.Add(new ProfileLine { Content = raw, Indent = indent, Number = number });
            }

            return result;
        }

        private static ProfileLine RequireLine(List<ProfileLine> lines, int i)
        {
            if (i >= lines.Count)
            {
                throw new SBoBCanonicalFormatException("truncated profile: unexpected end of file", 0);
            }
            return lines[i];
        }

        private static SBoBDialect ParseHeader(List<ProfileLine> lines, ref int i)
        {
            var apiLine = RequireLine(lines, i);
            var (apiKey, apiValue) = SplitKeyValue(apiLine);
            if (apiLine.Indent != 0 || apiKey != "apiVersion")
            {
                throw new SBoBCanonicalFormatException($"expected 'apiVersion: …' as the first line, got '{apiLine.Content}'", apiLine.Number);
            }

            SBoBDialect dialect;
            if (string.Equals(apiValue, KubescapeApiVersion, StringComparison.Ordinal))
            {
                dialect = SBoBDialect.KubescapeContainerProfile;
            }
            else if (string.Equals(apiValue, RegistryApiVersion, StringComparison.Ordinal))
            {
                dialect = SBoBDialect.RegistrySoftwareBillOfBehavior;
            }
            else
            {
                throw new SBoBCanonicalFormatException($"unknown apiVersion '{apiValue}' (expected '{KubescapeApiVersion}' or '{RegistryApiVersion}')", apiLine.Number);
            }

            i++;
            var kindLine = RequireLine(lines, i);
            var (kindKey, kindValue) = SplitKeyValue(kindLine);
            string expectedKind = dialect == SBoBDialect.KubescapeContainerProfile ? KubescapeKind : RegistryKind;
            if (kindLine.Indent != 0 || kindKey != "kind" || !string.Equals(kindValue, expectedKind, StringComparison.Ordinal))
            {
                throw new SBoBCanonicalFormatException(
                    $"expected 'kind: {expectedKind}' as the second line, got '{kindLine.Content}'", kindLine.Number);
            }

            i++;
            return dialect;
        }

        private static (string Name, string Namespace, IReadOnlyDictionary<string, string> Annotations) ParseMetadata(
            List<ProfileLine> lines, ref int i, SBoBDialect dialect)
        {
            var metadataLine = RequireLine(lines, i);
            if (metadataLine.Indent != 0 || metadataLine.Content != "metadata:")
            {
                throw new SBoBCanonicalFormatException($"expected 'metadata:', got '{metadataLine.Content}'", metadataLine.Number);
            }
            i++;

            var nameLine = RequireLine(lines, i);
            var name = ParseScalarToken(ExpectScalarLine(nameLine, 2, "name"), nameLine.Number);
            if (name.Length == 0)
            {
                throw new SBoBCanonicalFormatException("metadata.name must not be empty", nameLine.Number);
            }
            i++;

            var nsLine = RequireLine(lines, i);
            var metadataNamespace = ParseScalarToken(ExpectScalarLine(nsLine, 2, "namespace"), nsLine.Number);
            if (metadataNamespace.Length == 0)
            {
                throw new SBoBCanonicalFormatException("metadata.namespace must not be empty", nsLine.Number);
            }
            i++;

            var annotationsLine = RequireLine(lines, i);
            if (annotationsLine.Indent != 2 || annotationsLine.Content != "  annotations:")
            {
                throw new SBoBCanonicalFormatException($"expected 'annotations:', got '{annotationsLine.Content}'", annotationsLine.Number);
            }
            i++;

            var annotations = new Dictionary<string, string>(StringComparer.Ordinal);
            if (dialect == SBoBDialect.KubescapeContainerProfile)
            {
                var managedLine = RequireLine(lines, i);
                var managedToken = ExpectScalarLine(managedLine, 4, ManagedByAnnotation);
                if (managedToken != "User")
                {
                    throw new SBoBCanonicalFormatException($"annotation '{ManagedByAnnotation}' must be 'User', got '{managedToken}'", managedLine.Number);
                }
                annotations[ManagedByAnnotation] = "User";
                i++;
            }
            else
            {
                // The registry dialect swaps the kubescape UI marker for the registry spec-version marker (TDS §10.4).
                var specLine = RequireLine(lines, i);
                if (specLine.Indent != 4 || specLine.Content != $"    {SpecVersionAnnotation}: \"0.0.4\"")
                {
                    throw new SBoBCanonicalFormatException(
                        $"expected '    {SpecVersionAnnotation}: \"0.0.4\"' as the first registry-dialect annotation, got '{specLine.Content}'", specLine.Number);
                }
                annotations[SpecVersionAnnotation] = "0.0.4";
                i++;
            }

            // Fixed canonical annotation order: binding, generator, grammar-lock-sha256, behavior-hash.
            var bindingLine = RequireLine(lines, i);
            var bindingToken = ExpectScalarLine(bindingLine, 4, BindingAnnotation);
            if (bindingToken != PackagePurlBinding)
            {
                throw new SBoBCanonicalFormatException(
                    $"annotation '{BindingAnnotation}' must be '{PackagePurlBinding}' (this verifier only verifies package-purl-bound profiles), got '{bindingToken}'", bindingLine.Number);
            }
            annotations[BindingAnnotation] = PackagePurlBinding;
            i++;

            ParseAnnotationScalar(lines, ref i, GeneratorAnnotation, annotations);
            ParseAnnotationScalar(lines, ref i, GrammarLockAnnotation, annotations);
            ParseAnnotationScalar(lines, ref i, BehaviorHashAnnotation, annotations);

            return (name, metadataNamespace, annotations);
        }

        private static void ParseAnnotationScalar(List<ProfileLine> lines, ref int i, string key, Dictionary<string, string> annotations)
        {
            var line = RequireLine(lines, i);
            var token = ExpectScalarLine(line, 4, key);
            annotations[key] = ParseScalarToken(token, line.Number);
            i++;
        }

        private static void ParseSpec(
            List<ProfileLine> lines,
            ref int i,
            out string imageId,
            out IReadOnlyList<string> architectures,
            out IReadOnlyList<SBoBExecBehavior> execs,
            out IReadOnlyList<SBoBOpenBehavior> opens,
            out IReadOnlyList<SBoBNetworkBehavior> ingress,
            out IReadOnlyList<SBoBNetworkBehavior> egress)
        {
            var specLine = RequireLine(lines, i);
            if (specLine.Indent != 0 || specLine.Content != "spec:")
            {
                throw new SBoBCanonicalFormatException(
                    $"expected 'spec:' after the annotation block (no extra annotations are canonical), got '{specLine.Content}'", specLine.Number);
            }
            i++;

            architectures = Array.Empty<string>();
            var archLine = lines.Count > i ? lines[i] : null;
            if (archLine is not null && archLine.Indent == 2 && archLine.Content.StartsWith("  architectures: ", StringComparison.Ordinal))
            {
                architectures = ParseFlowList(archLine.Content["  architectures: ".Length..], archLine.Number);
                if (architectures.Count == 0)
                {
                    throw new SBoBCanonicalFormatException("architectures must not be empty when present (the canonical writer omits the section instead)", archLine.Number);
                }
                i++;
            }

            var imageLine = RequireLine(lines, i);
            imageId = ParseScalarToken(ExpectScalarLine(imageLine, 2, "imageID"), imageLine.Number);
            if (!PurlShape.IsMatch(imageId))
            {
                throw new SBoBCanonicalFormatException($"spec.imageID '{imageId}' is not a well-formed package purl (pkg:<type>/<name>@<version>)", imageLine.Number);
            }
            i++;

            // Sections in fixed canonical order; each parse consumes it or leaves the cursor untouched.
            var next = lines.Count > i ? lines[i] : null;
            execs = (next is not null && TrySection(next, "execs", 0)) ? ParseExecs(lines, ref i) : Array.Empty<SBoBExecBehavior>();
            next = lines.Count > i ? lines[i] : null;
            opens = (next is not null && TrySection(next, "opens", 1)) ? ParseOpens(lines, ref i) : Array.Empty<SBoBOpenBehavior>();
            next = lines.Count > i ? lines[i] : null;
            ingress = (next is not null && TrySection(next, "ingress", 2)) ? ParseNetSection(lines, ref i, "ingress") : Array.Empty<SBoBNetworkBehavior>();
            next = lines.Count > i ? lines[i] : null;
            egress = (next is not null && TrySection(next, "egress", 3)) ? ParseNetSection(lines, ref i, "egress") : Array.Empty<SBoBNetworkBehavior>();

            if (i < lines.Count && lines[i].Indent >= 4)
            {
                throw new SBoBCanonicalFormatException($"unexpected field after a section: '{lines[i].Content}'", lines[i].Number);
            }
        }

        private static IReadOnlyList<SBoBExecBehavior> ParseExecs(List<ProfileLine> lines, ref int i)
        {
            i++; // consume '  execs:'
            var result = new List<SBoBExecBehavior>();
            while (i < lines.Count && lines[i].Indent == 4 && lines[i].Content.StartsWith("    - ", StringComparison.Ordinal))
            {
                var pathLine = lines[i];
                var path = ParseScalarToken(ExpectSequenceLine(pathLine, 4, "path"), pathLine.Number);
                i++;

                var argsLine = RequireLine(lines, i);
                var argsToken = ExpectScalarLine(argsLine, 6, "args");
                var args = ParseFlowList(argsToken, argsLine.Number);
                if (args.Count == 0)
                {
                    throw new SBoBCanonicalFormatException("execs args must not be empty (the canonical writer falls back to [path, \"⋯⋯\"])", argsLine.Number);
                }
                i++;

                result.Add(new SBoBExecBehavior(path, args));
            }
            return result;
        }

        private static IReadOnlyList<SBoBOpenBehavior> ParseOpens(List<ProfileLine> lines, ref int i)
        {
            i++; // consume '  opens:'
            var result = new List<SBoBOpenBehavior>();
            while (i < lines.Count && lines[i].Indent == 4 && lines[i].Content.StartsWith("    - ", StringComparison.Ordinal))
            {
                var pathLine = lines[i];
                var path = ParseScalarToken(ExpectSequenceLine(pathLine, 4, "path"), pathLine.Number);
                i++;

                var flagsLine = RequireLine(lines, i);
                var flagsToken = ExpectScalarLine(flagsLine, 6, "flags");
                var flags = ParseFlowList(flagsToken, flagsLine.Number);
                i++;

                result.Add(new SBoBOpenBehavior(path, flags));
            }
            return result;
        }

        private static IReadOnlyList<SBoBNetworkBehavior> ParseNetSection(List<ProfileLine> lines, ref int i, string section)
        {
            i++; // consume '  <section>:'
            var result = new List<SBoBNetworkBehavior>();
            while (i < lines.Count && lines[i].Indent == 4 && lines[i].Content.StartsWith("    - ", StringComparison.Ordinal))
            {
                var idLine = lines[i];
                var identifier = ParseScalarToken(ExpectSequenceLine(idLine, 4, "identifier"), idLine.Number);
                i++;

                var typeLine = RequireLine(lines, i);
                var typeValue = ExpectScalarLine(typeLine, 6, "type");
                if (!string.Equals(typeValue, "external", StringComparison.Ordinal))
                {
                    throw new SBoBCanonicalFormatException(
                        $"net entry type must be 'external' in canonical output (v1 has no internal type), got '{typeValue}'", typeLine.Number);
                }
                i++;

                var dnsNames = new List<string>();
                var ipAddresses = new List<string>();
                var ports = new List<SBoBPortBehavior>();

                while (i < lines.Count && lines[i].Indent == 6)
                {
                    var line = lines[i];
                    var (key, value) = SplitKeyValue(line);
                    switch (key)
                    {
                        case "dnsNames":
                            if (dnsNames.Count > 0)
                            {
                                throw new SBoBCanonicalFormatException("duplicate dnsNames field", line.Number);
                            }
                            dnsNames = ParseFlowList(value, line.Number);
                            if (dnsNames.Count == 0)
                            {
                                throw new SBoBCanonicalFormatException("dnsNames must not be empty when present", line.Number);
                            }
                            break;
                        case "ipAddresses":
                            if (ipAddresses.Count > 0)
                            {
                                throw new SBoBCanonicalFormatException("duplicate ipAddresses field", line.Number);
                            }
                            ipAddresses = ParseFlowList(value, line.Number);
                            if (ipAddresses.Count == 0)
                            {
                                throw new SBoBCanonicalFormatException("ipAddresses must not be empty when present", line.Number);
                            }
                            break;
                        case "ports":
                            if (value.Length != 0)
                            {
                                throw new SBoBCanonicalFormatException("'ports:' must be followed by block-style port entries", line.Number);
                            }
                            i++;
                            while (i < lines.Count && lines[i].Indent == 8 && lines[i].Content.StartsWith("        - ", StringComparison.Ordinal))
                            {
                                ports.Add(ParsePortEntry(lines[i]));
                                i++;
                            }
                            if (ports.Count == 0)
                            {
                                throw new SBoBCanonicalFormatException("ports section must contain at least one port entry", line.Number);
                            }
                            continue; // i already advanced past the port entries
                        default:
                            throw new SBoBCanonicalFormatException(
                                $"unexpected, duplicate, or out-of-order net entry field '{key}' (canonical order: identifier, type, dnsNames, ipAddresses, ports)", line.Number);
                    }
                    i++;
                }

                result.Add(new SBoBNetworkBehavior(identifier, "external", dnsNames, ipAddresses, ports));
            }
            return result;
        }

        private static SBoBPortBehavior ParsePortEntry(ProfileLine line)
        {
            // Canonical port entry: "- { port: <int>, protocol: <scalar> }" (flow map, fixed field order).
            var content = line.Content["        - ".Length..];
            if (!content.StartsWith("{ ", StringComparison.Ordinal) || !content.EndsWith(" }", StringComparison.Ordinal))
            {
                throw new SBoBCanonicalFormatException($"port entry must be a flow map '{{ port: N, protocol: P }}', got '{content}'", line.Number);
            }
            var inner = content[2..^2];
            if (!inner.StartsWith("port: ", StringComparison.Ordinal))
            {
                throw new SBoBCanonicalFormatException($"port entry must start with 'port:' (fixed field order), got '{inner}'", line.Number);
            }
            inner = inner["port: ".Length..];
            int comma = inner.IndexOf(", protocol: ", StringComparison.Ordinal);
            if (comma < 0)
            {
                throw new SBoBCanonicalFormatException($"port entry must be 'port: N, protocol: P', got '{inner}'", line.Number);
            }
            var portToken = inner[..comma];
            var protocolToken = inner[(comma + ", protocol: ".Length)..];
            if (!int.TryParse(portToken, out int port) || port < 1 || port > 65535)
            {
                throw new SBoBCanonicalFormatException($"port must be an integer in [1, 65535], got '{portToken}'", line.Number);
            }
            var protocol = ParseScalarToken(protocolToken, line.Number);
            return new SBoBPortBehavior(port, protocol);
        }

        /// <summary>
        /// True when the line is the given section header. Throws fail-closed when the line is a spec
        /// section this parser does not know (capabilities/endpoints/rulePolicies/foreign keys) or a
        /// section that would break the fixed canonical order.
        /// </summary>
        private static bool TrySection(ProfileLine line, string section, int sectionIndex)
        {
            if (line.Indent != 2)
            {
                return false;
            }
            var body = line.Content.Trim();
            if (body.EndsWith(':') && !body.Contains(' '))
            {
                var key = body[..^1];
                var knownIndex = Array.IndexOf(KnownSections, key);
                if (knownIndex < 0)
                {
                    throw new SBoBCanonicalFormatException(
                        $"unknown spec section '{key}' (canonical order: execs, opens, ingress, egress; capabilities/endpoints/rulePolicies are not canonical for this verifier version)",
                        line.Number);
                }
                if (knownIndex < sectionIndex)
                {
                    throw new SBoBCanonicalFormatException(
                        $"spec section '{key}' is out of canonical order (it precedes '{section}')", line.Number);
                }
                return knownIndex == sectionIndex;
            }
            throw new SBoBCanonicalFormatException($"unexpected content in spec: '{line.Content}'", line.Number);
        }

        /// <summary>Returns the raw value token of a 'key: value' line at the expected indent with the expected key.</summary>
        private static string ExpectScalarLine(ProfileLine line, int indent, string key)
        {
            if (line.Indent != indent)
            {
                throw new SBoBCanonicalFormatException($"expected indentation of {indent} spaces, got {line.Indent} ('{line.Content}')", line.Number);
            }
            var (actualKey, value) = SplitKeyValue(line);
            if (actualKey != key)
            {
                throw new SBoBCanonicalFormatException($"expected key '{key}', got '{actualKey}'", line.Number);
            }
            return value;
        }

        /// <summary>Returns the raw value token of a block-sequence entry's first field ('- key: value'), enforcing the fixed canonical field order.</summary>
        private static string ExpectSequenceLine(ProfileLine line, int indent, string key)
        {
            if (line.Indent != indent || !line.Content.StartsWith($"{new string(' ', indent)}- ", StringComparison.Ordinal))
            {
                throw new SBoBCanonicalFormatException($"expected a block sequence entry at {indent}-space indent, got '{line.Content}'", line.Number);
            }
            var (actualKey, value) = SplitKeyValue(line);
            if (actualKey != key)
            {
                throw new SBoBCanonicalFormatException($"expected key '{key}' as the first field of the entry (fixed canonical field order), got '{actualKey}'", line.Number);
            }
            return value;
        }

        private static (string Key, string Value) SplitKeyValue(ProfileLine line)
        {
            var content = line.Content.TrimStart();
            if (content.StartsWith("- ", StringComparison.Ordinal))
            {
                content = content[2..];
            }

            int separator = content.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0)
            {
                if (content.EndsWith(':'))
                {
                    return (content[..^1], string.Empty);
                }
                throw new SBoBCanonicalFormatException($"expected 'key: value', got '{line.Content}'", line.Number);
            }
            // Keys never contain ": " in canonical output (values containing ": " are double-quoted).
            return (content[..separator], content[(separator + 2)..]);
        }

        /// <summary>Parse a canonical flow list "[a, b, c]". Every element must re-render byte-identically.</summary>
        private static List<string> ParseFlowList(string flow, int lineNumber)
        {
            if (flow.Length == 0 || flow[0] != '[' || flow[^1] != ']')
            {
                throw new SBoBCanonicalFormatException($"expected a flow list '[…]', got '{flow}'", lineNumber);
            }
            var content = flow[1..^1];
            var result = new List<string>();
            if (content.Length == 0)
            {
                return result;
            }

            var token = new StringBuilder();
            bool inQuotes = false;

            int position = 0;
            while (position < content.Length)
            {
                char c = content[position];
                if (inQuotes)
                {
                    token.Append(c);
                    if (c == '\\' && position + 1 < content.Length)
                    {
                        token.Append(content[position + 1]);
                        position += 2;
                        continue;
                    }
                    if (c == '"')
                    {
                        inQuotes = false;
                    }
                    position++;
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    token.Append(c);
                    position++;
                    continue;
                }

                if (c == ',')
                {
                    result.Add(ParseScalarToken(token.ToString(), lineNumber));
                    token.Clear();
                    if (position + 1 >= content.Length || content[position + 1] != ' ')
                    {
                        throw new SBoBCanonicalFormatException("flow list separators must be ', ' (comma followed by exactly one space)", lineNumber);
                    }
                    position += 2;
                    continue;
                }

                token.Append(c);
                position++;
            }

            if (inQuotes)
            {
                throw new SBoBCanonicalFormatException("unterminated double quote in flow list", lineNumber);
            }
            result.Add(ParseScalarToken(token.ToString(), lineNumber));
            return result;
        }

        /// <summary>
        /// Parse a scalar token (plain or double-quoted) and verify it re-renders byte-identically
        /// under the canonical writer's quoting rules (TDS §8.1). This rejects plain scalars the
        /// writer would have quoted, quoted scalars the writer would have left plain, unquoted
        /// U+22EF wildcards, and the ASCII "..." wildcard run.
        /// </summary>
        private static string ParseScalarToken(string token, int lineNumber)
        {
            if (token.Length == 0)
            {
                throw new SBoBCanonicalFormatException("empty scalar value", lineNumber);
            }

            string value;
            if (token[0] == '"')
            {
                if (token.Length < 2 || token[^1] != '"')
                {
                    throw new SBoBCanonicalFormatException("unterminated double-quoted scalar", lineNumber);
                }
                var sb = new StringBuilder(token.Length);
                var inner = token[1..^1];
                for (int p = 0; p < inner.Length; p++)
                {
                    char c = inner[p];
                    if (c == '\\')
                    {
                        if (p + 1 >= inner.Length)
                        {
                            throw new SBoBCanonicalFormatException("dangling escape in quoted scalar", lineNumber);
                        }
                        char escaped = inner[++p];
                        sb.Append(escaped switch
                        {
                            '"' => '"',
                            '\\' => '\\',
                            'n' => '\n',
                            't' => '\t',
                            _ => throw new SBoBCanonicalFormatException(
                                $"unsupported escape '\\{escaped}' in quoted scalar (the canonical writer emits only \\\" \\\\ \\n \\t)", lineNumber)
                        });
                        continue;
                    }
                    if (c < ' ')
                    {
                        throw new SBoBCanonicalFormatException("raw control character in scalar", lineNumber);
                    }
                    sb.Append(c);
                }
                value = sb.ToString();
            }
            else
            {
                value = token;
            }

            if (value == "...")
            {
                throw new SBoBCanonicalFormatException(
                    "ASCII '...' wildcard is never canonical — the SBoB wildcard is the single codepoint U+22EF (⋯)", lineNumber);
            }

            // Canonicality: the token must be exactly what the canonical writer would emit for this value.
            if (RenderScalar(value) != token)
            {
                throw new SBoBCanonicalFormatException(
                    $"non-canonical scalar rendering '{token}' (the canonical writer renders this value as '{RenderScalar(value)}')", lineNumber);
            }

            return value;
        }

        /// <summary>Canonical scalar rendering, ported from SBOB-Forge's CanonicalSbobEmitter (TDS §8.1).</summary>
        public static string RenderScalar(string value)
        {
            if (value.Length == 0)
            {
                return "\"\"";
            }
            return NeedsQuotes(value) ? Quote(value) : value;
        }

        private static bool NeedsQuotes(string v) =>
            v.Length == 0
            || char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[^1])
            || v.Contains(": ") || v.EndsWith(':')
            || v.Contains('#')
            || v.Contains(SegmentWildcard) || v.Contains('*')
            || v.Contains('\\')
            || v[0] is '?' or '-' or '!' or '&' or ',' or '[' or ']' or '{' or '}'
               or '|' or '>' or '\'' or '"' or '%' or '@' or '`';

        private static string Quote(string v)
        {
            var sb = new StringBuilder(v.Length + 2);
            sb.Append('"');
            foreach (var c in v)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
