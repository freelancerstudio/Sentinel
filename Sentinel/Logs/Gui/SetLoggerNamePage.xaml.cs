﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sentinel.Logs.Interfaces;
using Sentinel.Services;
using WpfExtras;

namespace Sentinel.Logs.Gui
{
    /// <summary>
    /// Interaction logic for SetLoggerNamePage.xaml
    /// </summary>
    public partial class SetLoggerNamePage : IWizardPage, IDataErrorInfo
    {
        private readonly ObservableCollection<IWizardPage> children = new ObservableCollection<IWizardPage>();

        private readonly ReadOnlyObservableCollection<IWizardPage> readonlyChildren;

        private string logName = "Untitled";
        private bool isValid;
        
        private readonly ILogManager logManager = ServiceLocator.Instance.Get<ILogManager>();

        public SetLoggerNamePage()
        {
            InitializeComponent();
            DataContext = this;
            readonlyChildren = new ReadOnlyObservableCollection<IWizardPage>(children);
            PropertyChanged += PropertyChangedHandler;
        }

        private void PropertyChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            if ( e.PropertyName == "LogName" )
            {
                // Validate against standard validation rules. 
                IsValid = this["LogName"] == null;
            }
        }

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                PropertyChangedEventArgs e = new PropertyChangedEventArgs(propertyName);
                handler(this, e);
            }
        }

        #endregion

        public string LogName
        {
            get
            {
                return logName;
            }
            set
            {
                if (logName == value) return;
                logName = value;
                OnPropertyChanged("LogName");
            }
        }

        #region Implementation of IWizardPage

        public string Title { get { return "Log Name"; } }

        public ReadOnlyObservableCollection<IWizardPage> Children
        {
            get
            {
                return readonlyChildren;
            }
        }

        public string Description
        {
            get
            {
                return "Define a name for the log to be created.";
            }
        }

        public bool IsValid
        {
            get
            {
                return isValid;
            }
            private set
            {
                if (isValid == value) return;
                isValid = value;
                OnPropertyChanged("IsValid");
            }
        }

        public Control PageContent
        {
            get
            {
                return this;
            }
        }

        public object Save(object saveData)
        {
            Debug.Assert(saveData is NewLoggerSettings, "Expecting to have a NewLoggerSettings instance");
            Debug.Assert(saveData as NewLoggerSettings != null, "Not expecting a null");

            NewLoggerSettings settings = saveData as NewLoggerSettings;
            if (settings != null)
            {
                settings.LogName = LogName;
            }

            return saveData;
        }

        public void AddChild(IWizardPage newItem)
        {
            children.Add(newItem);
            OnPropertyChanged("Children");
        }

        public void RemoveChild(IWizardPage item)
        {
            children.Remove(item);
            OnPropertyChanged("Children");
        }

        #endregion

        #region Implementation of IDataErrorInfo

        /// <summary>
        /// Gets the error message for the property with the given name.
        /// </summary>
        /// <returns>
        /// The error message for the property.
        /// </returns>
        /// <param name="columnName">The name of the property whose error message to get.</param>
        public string this[string columnName]
        {
            get
            {
                if ( columnName == "LogName" )
                {
                    if ( String.IsNullOrEmpty(LogName) )
                    {
                        return "Log name may not be blank.";
                    }

                    if ( logManager != null && logManager.Any(l => l.Name == LogName))
                    {
                        return "A logger with that name already exists";
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets an error message indicating what is wrong with this object.
        /// </summary>
        /// <returns>
        /// An error message indicating what is wrong with this object. The default is an empty string ("").
        /// </returns>
        public string Error
        {
            get
            {
                return this["LogName"];
            }
        }

        #endregion

        private void PageLoaded(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged("LogName");
        }
    }
}
