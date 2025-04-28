#region (c) 2019 Gilles Macabies All right reserved

//   Author     : Gilles Macabies
//   Solution   : DataGridFilter
//   Projet     : DataGridFilter
//   File       : ModelView.cs
//   Created    : 31/10/2019

#endregion (c) 2019 Gilles Macabies All right reserved

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
// ReSharper disable UnusedAutoPropertyAccessor.Global

// ReSharper disable MemberCanBePrivate.Global

namespace Demo
{
    public class ModelView : INotifyPropertyChanged
    {
        #region Private Fields

        private ICollectionView collView;
        private int count;
        private string search;

        #endregion Private Fields

        #region Public Constructors

        public ModelView(int i = 1000)
        {
            count = i;
            SelectedItem = count;
        }


        #endregion Public Constructors

        #region Public Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion Public Events

        #region Public Properties

        public ObservableCollection<Employee> Employees { get; set; }

        public ObservableCollection<Employee> FilteredList { get; set; }

        public int[] NumberItems { get; } = {10, 100, 1000, 10_000, 100_000, 500_000, 1_000_000 };

        /// <summary>
        ///     Refresh all
        /// </summary>
        public ICommand RefreshCommand => new DelegateCommand(RefreshData);

        /// <summary>
        ///     Global filter
        /// </summary>
        public string Search
        {
            get => search;
            set
            {
                search = value;

                collView.Filter = e =>
                {
                    var item = (Employee)e;
                    return item != null &&
                           ((item.LastName?.StartsWith(search, StringComparison.OrdinalIgnoreCase) ?? false)
                            || (item.FirstName?.StartsWith(search, StringComparison.OrdinalIgnoreCase) ?? false));
                };

                collView.Refresh();

                FilteredList = new ObservableCollection<Employee>(collView.OfType<Employee>());

                OnPropertyChanged("Search");
                OnPropertyChanged("FilteredList");
            }
        }

        public int SelectedItem
        {
            get => count;
            set
            {
                count = value;
                OnPropertyChanged(nameof(SelectedItem));
                Task.Run(FillData);
            }
        }

        #endregion Public Properties

        #region Private Methods

        /// <summary>
        ///     Fill data
        /// </summary>
        private async void FillData()
        {
            search = "";

            var employe = new List<Employee>(count);

            // for distinct lastname set "true" at CreateRandomEmployee(true)
            await Task.Run(() =>
            {
                for (var i = 0; i < count; i++)
                    employe.Add(RandomGenerator.CreateRandomEmployee(true));
            });

            Employees = new ObservableCollection<Employee>(employe);
            FilteredList = new ObservableCollection<Employee>(employe);
            FilteredList.RemoveAt(FilteredList.Count-1);
            collView = CollectionViewSource.GetDefaultView(FilteredList);

            OnPropertyChanged("Search");
            OnPropertyChanged("Employees");
            OnPropertyChanged("FilteredList");
        }

        private void OnPropertyChanged(string propertyname)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
        }

        /// <summary>
        ///     refresh data
        /// </summary>
        /// <param name="obj"></param>
        private void RefreshData(object obj)
        {
            collView = CollectionViewSource.GetDefaultView(new object());
            Task.Run(FillData);
        }

        #endregion Private Methods
    }
}