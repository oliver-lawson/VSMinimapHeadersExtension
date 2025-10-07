using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel;

namespace ScrollbarHeadersExtension
{
    // model that holds the actual settings
    internal class MinimapSettings : BaseOptionModel<MinimapSettings>
    {
        public bool ShowCommentHeaders { get; set; } = true;
        public bool ShowFunctions { get; set; } = true;
        public bool ShowClasses { get; set; } = true;
        public bool ShowAccessSpecifiers { get; set; } = false;
        public bool ShortenAccessSpecifiers { get; set; } = true;

        public static event EventHandler<SettingsChangedEventArgs> SettingsChanged;

        public void NotifyChanged()
        {
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs
            {
                ShowCommentHeaders = ShowCommentHeaders,
                ShowFunctions = ShowFunctions,
                ShowClasses = ShowClasses,
                ShowAccessSpecifiers = ShowAccessSpecifiers,
                ShortenAccessSpecifiers = ShortenAccessSpecifiers
            });
        }
    }

    // DialogPage wrapper for Tools > Options UI
    public class SettingsPage : DialogPage
    {
        [Category("Display Options")]
        [DisplayName("Show Comment Headers")]
        [Description("Display section headers from comments (lines starting with ==, --, ##, **)")]
        public bool ShowCommentHeaders
        {
            get => MinimapSettings.Instance.ShowCommentHeaders;
            set => MinimapSettings.Instance.ShowCommentHeaders = value;
        }

        [Category("Display Options")]
        [DisplayName("Show Functions")]
        [Description("Display function names")]
        public bool ShowFunctions
        {
            get => MinimapSettings.Instance.ShowFunctions;
            set => MinimapSettings.Instance.ShowFunctions = value;
        }

        [Category("Display Options")]
        [DisplayName("Show Classes")]
        [Description("Display class names")]
        public bool ShowClasses
        {
            get => MinimapSettings.Instance.ShowClasses;
            set => MinimapSettings.Instance.ShowClasses = value;
        }

        [Category("Display Options")]
        [DisplayName("Show Access Specifiers")]
        [Description("Display C++ access specifiers (public:, private:, protected:, signals:, slots:) - C++ files only")]
        public bool ShowAccessSpecifiers
        {
            get => MinimapSettings.Instance.ShowAccessSpecifiers;
            set => MinimapSettings.Instance.ShowAccessSpecifiers = value;
        }

        [Category("Formatting Options")]
        [DisplayName("Shorten Access Specifiers")]
        [Description("Abbreviate access specifiers (public=>pub:, protected=>prot, etc.)")]
        public bool ShortenAccessSpecifiers
        {
            get => MinimapSettings.Instance.ShortenAccessSpecifiers;
            set => MinimapSettings.Instance.ShortenAccessSpecifiers = value;
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                MinimapSettings.Instance.Save();
                MinimapSettings.Instance.NotifyChanged();
            }
            base.OnApply(e);
        }

        public override void LoadSettingsFromStorage()
        {
            MinimapSettings.Instance.Load();
        }
    }

    public class SettingsChangedEventArgs : EventArgs
    {
        public bool ShowCommentHeaders { get; set; }
        public bool ShowFunctions { get; set; }
        public bool ShowClasses { get; set; }
        public bool ShowAccessSpecifiers { get; set; }
        public bool ShortenAccessSpecifiers { get; set; }
    }
}