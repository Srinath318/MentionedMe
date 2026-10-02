using System;
using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;
using MentionedMe.Models;

namespace MentionedMe.Services
{
    public interface INotificationService
    {
        void ShowMentionNotification(MentionItem mention, bool playSound = true);
        void ShowAppNotification(string title, string message);
    }

    public class WindowsNotificationService : INotificationService
    {
        public void ShowMentionNotification(MentionItem mention, bool playSound = true)
        {
            if (mention == null) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddHeader("mentioned_header", "Mentioned Me?", "mentioned_args")
                    .AddText("Someone mentioned you")
                    .AddText($"\"{mention.Sentence}\"")
                    .AddAttributionText($"{mention.FormattedTime} • {mention.AudioSource}");

                if (!playSound)
                {
                    builder.AddAudio(new ToastAudio { Silent = true });
                }

                builder.Show(toast =>
                {
                    toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(5);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show toast notification: {ex.Message}");
            }
        }

        public void ShowAppNotification(string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show app toast: {ex.Message}");
            }
        }
    }
}
