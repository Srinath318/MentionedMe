using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using MentionedMe.Models;

namespace MentionedMe.Services
{
    public interface IMentionDetectionService
    {
        event EventHandler<MentionItem>? MentionDetected;
        void UpdateTargetNames(IEnumerable<string> names);
        void ProcessTranscript(string transcript);
        void ResetDeduplicationHistory();
    }

    public class MentionDetectionService : IMentionDetectionService
    {
        private readonly List<Regex> _compiledRegexes = new List<Regex>();
        private readonly List<string> _rawNames = new List<string>();
        private readonly object _lock = new object();

        // Deduplication history (holds recent detections for 6.0 seconds)
        private readonly List<(DateTime time, string matchedName, string normalizedSentence)> _recentDetections = 
            new List<(DateTime, string, string)>();
        private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromSeconds(6.0);

        public event EventHandler<MentionItem>? MentionDetected;

        public void UpdateTargetNames(IEnumerable<string> names)
        {
            lock (_lock)
            {
                _compiledRegexes.Clear();
                _rawNames.Clear();

                foreach (var name in names ?? Enumerable.Empty<string>())
                {
                    var trimmed = name.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    _rawNames.Add(trimmed);

                    // Build canonical variations for high accuracy with zero false positives
                    var variations = GenerateNameVariations(trimmed);
                    var regexPatterns = string.Join("|", variations.Select(v => Regex.Escape(v)));
                    var pattern = $@"\b(?:{regexPatterns})(?:'s)?\b";
                    _compiledRegexes.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }
            }
        }

        public void ProcessTranscript(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return;

            lock (_lock)
            {
                if (_rawNames.Count == 0) return;

                PruneOldDetections();

                var sentences = SplitIntoSentences(transcript);

                foreach (var sentence in sentences)
                {
                    if (string.IsNullOrWhiteSpace(sentence)) continue;

                    bool matchFound = false;

                    // 1. Exact & Canonical Regex Matching (Fastest & 100% precision)
                    for (int i = 0; i < _compiledRegexes.Count; i++)
                    {
                        var regex = _compiledRegexes[i];
                        var match = regex.Match(sentence);

                        if (match.Success)
                        {
                            var matchedName = _rawNames[i];
                            TriggerMention(matchedName, sentence);
                            matchFound = true;
                            break;
                        }
                    }

                    if (matchFound) continue;

                    // 2. High-Precision Single-Word & 2-Word Split Matching (Levenshtein distance <= 1 with strict length guards)
                    var words = ExtractWords(sentence);
                    for (int w = 0; w < words.Count; w++)
                    {
                        if (matchFound) break;
                        var word = words[w];

                        for (int i = 0; i < _rawNames.Count; i++)
                        {
                            var targetName = _rawNames[i];

                            // 2a. Single-word fuzzy match (strictly length +-1, max edit distance 1)
                            if (IsHighPrecisionMatch(word, targetName))
                            {
                                TriggerMention(targetName, sentence);
                                matchFound = true;
                                break;
                            }

                            // 2b. Two-word split match (e.g. "Sri Nath", "Shri Nath")
                            if (w + 1 < words.Count)
                            {
                                var clean1 = Regex.Replace(words[w], @"['’]s?$", "");
                                var clean2 = Regex.Replace(words[w + 1], @"['’]s?$", "");
                                var twoWordsMerged = clean1 + clean2;

                                if (IsHighPrecisionMatch(twoWordsMerged, targetName))
                                {
                                    TriggerMention(targetName, sentence);
                                    matchFound = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void TriggerMention(string matchedName, string sentence)
        {
            var normalized = NormalizeForComparison(sentence);

            if (IsDuplicate(matchedName, normalized))
            {
                Debug.WriteLine($"[DIAG] 🔁 Duplicate mention suppressed: '{matchedName}' in \"{sentence}\"");
                return;
            }

            _recentDetections.Add((DateTime.UtcNow, matchedName, normalized));

            var mentionItem = new MentionItem
            {
                Timestamp = DateTime.Now,
                Sentence = CleanSentence(sentence, matchedName),
                MatchedName = matchedName,
                AudioSource = "Windows System Audio"
            };

            Debug.WriteLine($"[DIAG] 🎯 NAME DETECTED: '{matchedName}' at {DateTime.Now:HH:mm:ss.fff} in \"{sentence}\"");

            MentionDetected?.Invoke(this, mentionItem);
        }

        private static bool IsHighPrecisionMatch(string candidateWord, string targetName)
        {
            if (string.IsNullOrWhiteSpace(candidateWord) || string.IsNullOrWhiteSpace(targetName))
                return false;

            var cand = candidateWord.Trim().ToLowerInvariant();
            var target = targetName.Trim().ToLowerInvariant();

            // Strip trailing possessive 's or punctuation
            if (cand.EndsWith("'s")) cand = cand.Substring(0, cand.Length - 2);

            if (cand == target) return true;

            // Direct substring containment for joined compound words (e.g. "venshrinath" containing "shrinath")
            if (target.Length >= 5 && cand.Length > target.Length)
            {
                var variations = GenerateNameVariations(target);
                if (variations.Any(v => cand.Contains(v.ToLowerInvariant())))
                {
                    return true;
                }
            }

            // Normalize common initial clusters (sh <-> s, ph <-> f, c <-> k)
            var normCand = Regex.Replace(cand, @"^sh(?=[rRlL]|[aeiouy])", "s");
            var normTarget = Regex.Replace(target, @"^sh(?=[rRlL]|[aeiouy])", "s");

            normCand = Regex.Replace(normCand, @"ph", "f");
            normTarget = Regex.Replace(normTarget, @"ph", "f");

            normCand = Regex.Replace(normCand, @"^c(?=[aou])", "k");
            normTarget = Regex.Replace(normTarget, @"^c(?=[aou])", "k");

            // Normalize terminal dental (th <-> t <-> dh)
            normCand = Regex.Replace(normCand, @"(th|dh)$", "t");
            normTarget = Regex.Replace(normTarget, @"(th|dh)$", "t");

            if (normCand == normTarget) return true;

            // Suffix check for compound words (e.g. "venshrinath" -> suffix "shrinath")
            if (normTarget.Length >= 5 && normCand.Length > normTarget.Length && normCand.EndsWith(normTarget))
            {
                return true;
            }

            // Strict length guard on normalized strings
            if (Math.Abs(normCand.Length - normTarget.Length) > 2)
            {
                return false;
            }

            // Names <= 3 chars require exact match
            if (target.Length <= 3)
            {
                return false;
            }

            // First character must match
            if (normCand.Length == 0 || normTarget.Length == 0 || normCand[0] != normTarget[0])
            {
                return false;
            }

            int dist = ComputeLevenshteinDistance(normCand, normTarget);
            if (dist <= 1)
            {
                return true;
            }

            // Consonant skeleton check (vowels ignored)
            var candConsonants = Regex.Replace(normCand, @"[aeiouy]+", "*");
            var targetConsonants = Regex.Replace(normTarget, @"[aeiouy]+", "*");
            if (candConsonants == targetConsonants && dist <= 2)
            {
                return true;
            }

            return false;
        }

        private static List<string> GenerateNameVariations(string name)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
            var lower = name.ToLowerInvariant();

            // Common Whisper STT phonetic variations for Indian and international names
            if (lower == "srinath")
            {
                results.Add("Sreenath");
                results.Add("Shrinath");
                results.Add("Shreenath");
                results.Add("Srinith");
                results.Add("Srinaeth");
                results.Add("Srinatha");
                results.Add("Srinat");
                results.Add("Sreenadh");
                results.Add("Shrinadh");
                results.Add("Sri Nath");
                results.Add("Shri Nath");
                results.Add("Sree Nath");
            }
            else if (lower == "srinivas" || lower == "srinivasan")
            {
                results.Add("Sreenivas");
                results.Add("Shrinivas");
                results.Add("Sreenivasan");
                results.Add("Shrinivasan");
                results.Add("Sri Nivas");
            }
            else if (lower == "catherine" || lower == "katherine")
            {
                results.Add("Catherine");
                results.Add("Katherine");
                results.Add("Kathryn");
                results.Add("Cathryn");
            }
            else if (lower == "stephen" || lower == "steven")
            {
                results.Add("Stephen");
                results.Add("Steven");
            }
            else if (lower == "alexander" || lower == "aleksander")
            {
                results.Add("Alexander");
                results.Add("Aleksander");
            }
            else if (lower == "priya" || lower == "preeya")
            {
                results.Add("Priya");
                results.Add("Preeya");
            }
            else if (lower == "deepak" || lower == "dipak")
            {
                results.Add("Deepak");
                results.Add("Dipak");
            }

            return results.ToList();
        }

        private static int ComputeLevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0) return m;
            if (m == 0) return n;

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        private static List<string> ExtractWords(string sentence)
        {
            var words = new List<string>();
            var matches = Regex.Matches(sentence, @"\b[\w'-]+\b");
            foreach (Match m in matches)
            {
                if (m.Success && !string.IsNullOrWhiteSpace(m.Value))
                {
                    words.Add(m.Value);
                }
            }
            return words;
        }

        private bool IsDuplicate(string matchedName, string normalizedSentence)
        {
            var now = DateTime.UtcNow;
            foreach (var (time, name, sentence) in _recentDetections)
            {
                var elapsed = now - time;
                if (elapsed > DeduplicationWindow) continue;

                if (string.Equals(name, matchedName, StringComparison.OrdinalIgnoreCase))
                {
                    // Within 3.0 seconds (rolling audio window overlap), duplicate detections are suppressed
                    if (elapsed < TimeSpan.FromSeconds(3.0))
                    {
                        return true;
                    }

                    if (sentence.Contains(normalizedSentence) || normalizedSentence.Contains(sentence))
                    {
                        return true;
                    }

                    if (CalculateJaccardSimilarity(sentence, normalizedSentence) >= 0.30)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void PruneOldDetections()
        {
            var cutoff = DateTime.UtcNow - DeduplicationWindow;
            _recentDetections.RemoveAll(d => d.time < cutoff);
        }

        public void ResetDeduplicationHistory()
        {
            lock (_lock)
            {
                _recentDetections.Clear();
            }
        }

        private static List<string> SplitIntoSentences(string text)
        {
            var sentences = new List<string>();
            var rawParts = Regex.Split(text, @"(?<=[.!?])\s+");

            foreach (var part in rawParts)
            {
                var clean = part.Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    sentences.Add(clean);
                }
            }

            if (sentences.Count == 0 && !string.IsNullOrWhiteSpace(text))
            {
                sentences.Add(text.Trim());
            }

            return sentences;
        }

        private static string CleanSentence(string sentence, string matchedName)
        {
            var trimmed = sentence.Trim();
            if (trimmed.Length > 0 && char.IsLower(trimmed[0]))
            {
                trimmed = char.ToUpper(trimmed[0]) + trimmed.Substring(1);
            }

            return trimmed;
        }

        private static string NormalizeForComparison(string text)
        {
            var alphanumeric = Regex.Replace(text.ToLowerInvariant(), @"[^\w\s]", "");
            return Regex.Replace(alphanumeric, @"\s+", " ").Trim();
        }

        private static double CalculateJaccardSimilarity(string a, string b)
        {
            var wordsA = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var wordsB = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            if (wordsA.Count == 0 || wordsB.Count == 0) return 0.0;

            int intersection = wordsA.Count(w => wordsB.Contains(w));
            int union = wordsA.Union(wordsB).Count();

            return (double)intersection / union;
        }
    }
}
