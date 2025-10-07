using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Settings;
using System;
using System.Reflection;
using System.Linq;

namespace ScrollbarHeadersExtension
{
    internal abstract class BaseOptionModel<T> where T : BaseOptionModel<T>, new()
    {
        private static T _instance;

        protected BaseOptionModel() { }

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new T();
                    _instance.Load();
                }
                return _instance;
            }
        }

        protected virtual string CollectionName => typeof(T).FullName;

        public virtual void Load()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            var settingsStore = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

            // no saved settings found?
            if (!settingsStore.CollectionExists(CollectionName))
                return;

            foreach (var property in GetOptionProperties())
            {
                try
                {
                    if (settingsStore.PropertyExists(CollectionName, property.Name))
                    {
                        var value = settingsStore.GetBoolean(CollectionName, property.Name);
                        property.SetValue(this, value);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading {property.Name}: {ex.Message}");
                }
            }
        }

        public virtual void Save()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
            var settingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

            if (!settingsStore.CollectionExists(CollectionName))
                settingsStore.CreateCollection(CollectionName);

            foreach (var property in GetOptionProperties())
            {
                var value = (bool)property.GetValue(this);
                settingsStore.SetBoolean(CollectionName, property.Name, value);
            }
        }

        private PropertyInfo[] GetOptionProperties()
        {
            return GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(bool) && p.CanRead && p.CanWrite)
                .ToArray();
        }
    }
}