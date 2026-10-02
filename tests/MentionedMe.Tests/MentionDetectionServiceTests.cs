using System;
using System.Collections.Generic;
using MentionedMe.Models;
using MentionedMe.Services;
using Xunit;

namespace MentionedMe.Tests
{
    public class MentionDetectionServiceTests
    {
        [Fact]
        public void DetectsTargetName_InSentence()
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "Srinath" });

            MentionItem? detected = null;
            service.MentionDetected += (s, item) => detected = item;

            service.ProcessTranscript("Hey Srinath, can you look at this pull request?");

            Assert.NotNull(detected);
            Assert.Equal("Srinath", detected.MatchedName);
            Assert.Contains("Srinath", detected.Sentence);
        }

        [Fact]
        public void DetectsTargetName_CaseInsensitive()
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "alex" });

            MentionItem? detected = null;
            service.MentionDetected += (s, item) => detected = item;

            service.ProcessTranscript("I think ALEX is working on the backend.");

            Assert.NotNull(detected);
            Assert.Equal("alex", detected.MatchedName);
        }

        [Fact]
        public void DetectsPossessiveName()
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "Jordan" });

            MentionItem? detected = null;
            service.MentionDetected += (s, item) => detected = item;

            service.ProcessTranscript("That was Jordan's proposal from yesterday.");

            Assert.NotNull(detected);
            Assert.Equal("Jordan", detected.MatchedName);
        }

        [Fact]
        public void IgnoresSubstringMatches_WithWordBoundaries()
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "Dan" });

            MentionItem? detected = null;
            service.MentionDetected += (s, item) => detected = item;

            // "Dan" should NOT match "Dangerous" or "Guidance"
            service.ProcessTranscript("That approach is dangerous and lacks proper guidance.");

            Assert.Null(detected);
        }

        [Fact]
        public void DeduplicatesIdenticalMentions_WithinTimeWindow()
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "Sarah" });

            int detectionCount = 0;
            service.MentionDetected += (s, item) => detectionCount++;

            // First chunk
            service.ProcessTranscript("Sarah let's review the quarterly numbers.");
            // Overlapping consecutive window
            service.ProcessTranscript("Sarah let's review the quarterly numbers.");

            Assert.Equal(1, detectionCount);
        }

        [Fact]
        public void DetectsMultipleAliases()
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "Christopher", "Chris", "Topher" });

            var detections = new List<MentionItem>();
            service.MentionDetected += (s, item) => detections.Add(item);

            service.ProcessTranscript("Thanks Chris for the update.");
            service.ProcessTranscript("Also Topher will follow up later.");

            Assert.Equal(2, detections.Count);
            Assert.Equal("Chris", detections[0].MatchedName);
            Assert.Equal("Topher", detections[1].MatchedName);
        }
        [Theory]
        [InlineData("I think we should send this to Shrienath after this.")]
        [InlineData("during this discussion Shri Naut will need to be done")]
        [InlineData("The user of Venshrinath is actually mentioned.")]
        [InlineData("We discussed this with Shrinath yesterday.")]
        [InlineData("Is Sreenadh available on the call?")]
        public void DetectsVariousPhoneticDistortions(string transcript)
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { "Srinath" });

            MentionItem? detected = null;
            service.MentionDetected += (s, item) => detected = item;

            service.ProcessTranscript(transcript);

            Assert.NotNull(detected);
            Assert.Equal("Srinath", detected.MatchedName);
        }

        [Theory]
        [InlineData("Katherine", "Let's ask Catherine about the deployment.", "Katherine")]
        [InlineData("Aleksander", "Alexander has updated the PR.", "Alexander")]
        [InlineData("Karlos", "We will meet Carlos at noon.", "Carlos")]
        [InlineData("Dipak", "Deepak is leading the design.", "Deepak")]
        [InlineData("Preeya", "Has Priya signed off on the release?", "Priya")]
        [InlineData("Steven", "Stephen wrote the unit tests.", "Stephen")]
        public void DetectsUniversalNames_FuzzyAndPhonetic(string candidateInSpeech, string sentence, string targetName)
        {
            var service = new MentionDetectionService();
            service.UpdateTargetNames(new[] { targetName });

            MentionItem? detected = null;
            service.MentionDetected += (s, item) => detected = item;

            service.ProcessTranscript(sentence);

            Assert.NotNull(detected);
            Assert.Equal(targetName, detected.MatchedName);
        }
    }
}
