﻿using System.ComponentModel;
using Newtonsoft.Json.Linq;
using Sentinel.Classification;
using Sentinel.Classification.Interfaces;
using Sentinel.Extractors;
using Sentinel.Extractors.Interfaces;
using Sentinel.Filters;
using Sentinel.Filters.Interfaces;
using Sentinel.Highlighters;
using Sentinel.Highlighters.Interfaces;
using Sentinel.Images;
using Sentinel.Images.Interfaces;
using Sentinel.Interfaces;
using Sentinel.Interfaces.Providers;
using Sentinel.Logger;
using Sentinel.Logs;
using Sentinel.Logs.Gui;
using Sentinel.Logs.Interfaces;
using Sentinel.NLog;
using Sentinel.Preferences;
using Sentinel.Providers;
using Sentinel.Providers.Interfaces;
using Sentinel.Services.Interfaces;
using Sentinel.Support;
using Sentinel.Support.Mvvm;
using Sentinel.Views;
using Sentinel.Views.Gui;
using Sentinel.Views.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows;
using Sentinel.Log4Net;

namespace Sentinel.Services
{
    [DataContract]
    public class SessionManager : ISessionManager
    {
        private const char ObjectSeparator = '~';
        private bool _serviceLocatorIsFresh;

        public SessionManager()
        {
            Name = "Untitled";
            RefreshServiceLocator();
        }

        public IEnumerable<ViewModelBase> ChangingViewModelBases { get; set; }

        public bool IsSaved { get; set; }

        public string Name { get; private set; }

        public IEnumerable<IProviderSettings> ProviderSettings
        {
            get
            {
                var providerManager = ServiceLocator.Instance.Get<IProviderManager>();
                return providerManager.GetInstances().Select(c => c.ProviderSettings);
            }
        }

        public void LoadNewSession(Window parent)
        {
            if (!_serviceLocatorIsFresh)
            {
                CleanUpResources();
                Name = "Untitled";
                RefreshServiceLocator();
            }

            var wizard = new NewLoggerWizard();

            if (!wizard.Display(parent))
                return;

            var settings = wizard.Settings;

            //Set session properties
            Name = settings.LogName;

            ConfigureLoggerServices(settings.LogName, settings.Views, settings.Providers);

            IsSaved = false;
            _serviceLocatorIsFresh = false;
        }

        public void LoadSession(string fileName)
        {
            /*var fi = new FileInfo(fileName);
            File.ReadAllText will already throw FileNotFoundException
            /*if (!fi.Exists) 
                throw new FileNotFoundException();
            
            using (var fs = fi.OpenRead())
            using (var sr = new StreamReader(fs))
            {
                fileText = sr.ReadToEnd();
            }*/

            string fileText = File.ReadAllText(fileName);
            string[] jsonObjects = fileText.Split(ObjectSeparator);

            CleanUpResources();
            LoadServiceLocator(jsonObjects);

            IsSaved = false;
        }

        public void SaveSession(string filePath)
        {
            using (var writer = File.CreateText(filePath))
            {
                var values = ServiceLocator.Instance.RegisteredServices
                    .Where(value => value.HasAttribute<DataContractAttribute>());

                foreach (object value in values)
                {
                    writer.WriteLine(JsonHelper.SerializeToString(value));
                    writer.WriteLine(ObjectSeparator); //Object separator?
                }
            }

            IsSaved = true;
        }

        private void CleanUpResources()
        {
            // Close all open providers
            var providerManager = ServiceLocator.Instance.Get<IProviderManager>();
            foreach (var provider in providerManager.GetInstances())
            {
                provider.Close();
            }

            // Unregister changing viewmodelbases
            foreach (var viewmodel in ChangingViewModelBases)
            {
                viewmodel.PropertyChanged -= ViewModelProperty_Changed;
            }
        }

        private static void ConfigureLoggerServices(string logName, IEnumerable<string> viewIdentifiers, IEnumerable<PendingProviderRecord> pendingProviderRecords)
        {
            // Create the logger.
            var logManager = ServiceLocator.Instance.Get<ILogManager>();
            ILogger log = logManager.Add(logName);

            // Create the frame view
            var viewManager = ServiceLocator.Instance.Get<IViewManager>();
            Debug.Assert(
                viewManager != null,
                "A ViewManager should be registered with service locator for the IViewManager interface");

            var frame = ServiceLocator.Instance.Get<IWindowFrame>();
            frame.Log = log;
            frame.SetViews(viewIdentifiers);
            viewManager.Viewers.Add(frame);

            // Create the providers.
            var providerManager = ServiceLocator.Instance.Get<IProviderManager>();
            foreach (var providerRecord in pendingProviderRecords)
            {
                var provider = providerManager.Create(providerRecord.Info.Identifier, providerRecord.Settings);
                provider.Logger = log;
                provider.Start();
            }
        }

        private void LoadChangingViewModelBases()
        {
            var viewModelBases = new List<ViewModelBase>();
            var locator = ServiceLocator.Instance;
            viewModelBases.Add((SearchExtractor)locator.Get<ISearchExtractor>());
            viewModelBases.Add((SearchFilter)locator.Get<ISearchFilter>());
            viewModelBases.Add((HighlightingService<IHighlighter>)locator.Get<IHighlightingService<IHighlighter>>());
            viewModelBases.Add((ExtractingService<IExtractor>)locator.Get<IExtractingService<IExtractor>>());
            viewModelBases.Add((FilteringService<IFilter>)locator.Get<IFilteringService<IFilter>>());
            viewModelBases.Add((ClassifyingService<IClassifier>)locator.Get<IClassifyingService<IClassifier>>());

            ChangingViewModelBases = viewModelBases;

            foreach (var item in ChangingViewModelBases)
            {
                item.PropertyChanged += ViewModelProperty_Changed;
            }
        }

        private void LoadServiceLocator(IEnumerable<string> jsonObjectStrings)
        {
            if (jsonObjectStrings == null) return;

            var locator = ServiceLocator.Instance;
            var pendingProviderRecords = new List<PendingProviderRecord>();

            foreach (var objString in jsonObjectStrings)
            {
                if (!string.IsNullOrWhiteSpace(objString))
                {
                    var deserializedObj = JObject.Parse(objString);
                    var typeString = deserializedObj["$type"].ToString();

                    if (typeString.Contains(typeof(UserPreferences).ToString())) locator.Register<IUserPreferences>(JsonHelper.DeserializeFromString<UserPreferences>(objString));
                    else if (typeString.Contains(typeof(SearchFilter).Name)) locator.Register<ISearchFilter>(JsonHelper.DeserializeFromString<SearchFilter>(objString));
                    else if (typeString.Contains(typeof(SearchExtractor).Name)) locator.Register<ISearchExtractor>(JsonHelper.DeserializeFromString<SearchExtractor>(objString));
                    else if (typeString.Contains(typeof(FilteringService<>).Name)) locator.Register<IFilteringService<IFilter>>(JsonHelper.DeserializeFromString<FilteringService<IFilter>>(objString));
                    else if (typeString.Contains(typeof(ExtractingService<>).Name)) locator.Register<IExtractingService<IExtractor>>(JsonHelper.DeserializeFromString<ExtractingService<IExtractor>>(objString));
                    else if (typeString.Contains(typeof(HighlightingService<>).Name)) locator.Register<IHighlightingService<IHighlighter>>(JsonHelper.DeserializeFromString<HighlightingService<IHighlighter>>(objString));
                    else if (typeString.Contains(typeof(SearchHighlighter).Name)) locator.Register<ISearchHighlighter>(JsonHelper.DeserializeFromString<SearchHighlighter>(objString));
                    else if (typeString.Contains(typeof(ClassifyingService<>).Name)) locator.Register<IClassifyingService<IClassifier>>(JsonHelper.DeserializeFromString<ClassifyingService<IClassifier>>(objString));
                    else if (typeString.Contains(typeof(TypeToImageService).Name)) locator.Register<ITypeImageService>(JsonHelper.DeserializeFromString<TypeToImageService>(objString));
                    else if (typeString.Contains(typeof(SessionManager).Name))
                    {
                        Name = deserializedObj["Name"].ToString();

                        LoadChangingViewModelBases();

                        var providerSettingsObj = deserializedObj["ProviderSettings"].HasValues ? deserializedObj["ProviderSettings"].Values() : null;

                        if (providerSettingsObj == null)
                            continue;

                        var providerInstances = providerSettingsObj.Last();
                        foreach (var providerSetting in providerInstances)
                        {
                            if (providerSetting["$type"].ToString().Contains(typeof(NetworkSettings).Name))
                            {
                                var thisSetting = JsonHelper.DeserializeFromString<NetworkSettings>(providerSetting.ToString());
                                pendingProviderRecords.Add(new PendingProviderRecord()
                                {
                                    Info = thisSetting.Info,
                                    Settings = thisSetting
                                });
                            }
                            else if (providerSetting["$type"].ToString().Contains(typeof(UdpAppenderSettings).Name))
                            {
                                var thisSetting = JsonHelper.DeserializeFromString<UdpAppenderSettings>(providerSetting.ToString());
                                pendingProviderRecords.Add(new PendingProviderRecord()
                                {
                                    Info = thisSetting.Info,
                                    Settings = thisSetting
                                });
                            }
                            
                        }
                    }
                }
            }

            //Load new objects for the rest.            
            locator.Register<ILogManager>(new LogManager());
            locator.Register<LogWriter>(new LogWriter());
            locator.Register(typeof(IViewManager), typeof(ViewManager), false);
            locator.Register<IProviderManager>(new ProviderManager());
            locator.Register<IWindowFrame>(new MultipleViewFrame()); //needs IUserPreferences, IViewManager
            locator.Register<ILogFileExporter>(new LogFileExporter());
            locator.Register<INewProviderWizard>(new NewProviderWizard());

            // Do this last so that other services have registered, e.g. the
            // TypeImageService is called by some classifiers!);););
            if (!locator.IsRegistered<IClassifyingService<IClassifier>>())
            {
                locator.Register(typeof(IClassifyingService<IClassifier>), typeof(ClassifyingService<IClassifier>), true);
            }

            var viewIDs = new List<String> { locator.Get<IViewManager>().GetRegistered().First().Identifier };

            ConfigureLoggerServices(Name, viewIDs, pendingProviderRecords);

            GC.Collect(); //collect all things without a reference

            _serviceLocatorIsFresh = false;
        }

        private void RefreshServiceLocator()
        {
            var locator = ServiceLocator.Instance;

            locator.Register(typeof(IUserPreferences), typeof(UserPreferences), true);
            locator.Register(typeof(ISearchFilter), typeof(SearchFilter), true);
            locator.Register(typeof(ISearchExtractor), typeof(SearchExtractor), true);
            locator.Register(typeof(IFilteringService<IFilter>), typeof(FilteringService<IFilter>), true);
            locator.Register(typeof(IExtractingService<IExtractor>), typeof(ExtractingService<IExtractor>), true);
            locator.Register(typeof(IHighlightingService<IHighlighter>), typeof(HighlightingService<IHighlighter>), true);
            locator.Register(typeof(ISearchHighlighter), typeof(SearchHighlighter), true);
            locator.Register(typeof(IClassifyingService<IClassifier>), typeof(ClassifyingService<IClassifier>), true);

            locator.Register(typeof(ITypeImageService), typeof(TypeToImageService), true);
            locator.Register<ILogManager>(new LogManager());
            locator.Register<LogWriter>(new LogWriter());
            locator.Register(typeof(IViewManager), typeof(ViewManager), false);
            locator.Register<IProviderManager>(new ProviderManager());
            locator.Register<IWindowFrame>(new MultipleViewFrame()); //needs IUserPreferences, IViewManager
            locator.Register<ILogFileExporter>(new LogFileExporter());

            locator.Register<INewProviderWizard>(new NewProviderWizard());

            LoadChangingViewModelBases();

            GC.Collect(); //collect all things without a reference

            _serviceLocatorIsFresh = true;
        }

        private void ViewModelProperty_Changed(object sender, PropertyChangedEventArgs e)
        {
            IsSaved = false;
            _serviceLocatorIsFresh = false;
        }
    }
}