using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace DemoApp
{
    public class DataGridClipboardBehavior
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(DataGridClipboardBehavior)
                , new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid dataGrid)
            {
                if ((bool)e.NewValue)
                {
                    dataGrid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
                        OnPasteExecuted, OnPasteCanExecute));
                }
                else
                {

                }
            }
        }

        private static void OnPasteCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = sender is DataGrid && Clipboard.ContainsText();
        }

        private static void OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                PasteClipboardToDataGrid(dataGrid);
            }
        }

        /// <summary>
        /// get & process data 
        /// </summary>
        /// <param name="dataGrid"></param>
        private static void PasteClipboardToDataGrid(DataGrid dataGrid)
        {
            // get clipboard data
            var clipboardText = Clipboard.GetText();
            var clipboardLines = clipboardText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // get current cell position
            var currentCell = dataGrid.CurrentCell;
            if (currentCell == null || currentCell.Item == null)
            {
                return;
            }

            var startRowIndex = dataGrid.Items.IndexOf(currentCell.Item);
            var startColumnIndex = dataGrid.Columns.IndexOf(currentCell.Column);

            // process each line (rows)
            for (int i = 0; i < clipboardLines.Length; i++)
            {
                var rowIndex = startRowIndex + i;
                if (rowIndex >= dataGrid.Items.Count)
                {
                    break;
                }

                var currentItem = dataGrid.Items[rowIndex];
                var values = clipboardLines[i].Split('\t');

                // process values in line (columns)
                for (int j = 0; j < values.Length; j++)
                {
                    var columnIndex = startColumnIndex + j;
                    if (columnIndex >= dataGrid.Columns.Count)
                    {
                        break;
                    }
                    var column = dataGrid.Columns[columnIndex];
                    SetCellValue(currentItem, column, values[j]);
                }
            }
        }

        /// <summary>
        /// Set data to cell
        /// </summary>
        /// <param name="item"></param>
        /// <param name="column"></param>
        /// <param name="value"></param>
        private static void SetCellValue(object item, DataGridColumn column, string value)
        {
            // get property path
            string propertyPath = GetColumnPropertyPath(column);

            if (string.IsNullOrEmpty(propertyPath))
            {
                return;
            }

            // get property info
            PropertyInfo propertyInfo = GetPropertyFromPath(item.GetType(), propertyPath);

            if (propertyInfo == null)
            {
                return;
            }

            // convert to destination type
            object convertedValue = ConvertValueToPropertyType(value, propertyInfo.PropertyType);
            propertyInfo.SetValue(item, convertedValue);

            NotifyPropertyChanged(item, propertyPath);
        }

        /// <summary>
        /// get binding path of copied column 
        /// </summary>
        /// <param name="column"></param>
        /// <returns></returns>
        private static string GetColumnPropertyPath(DataGridColumn column)
        {
            if (column is DataGridBoundColumn boundColumn)
            {
                var binding = boundColumn.Binding as Binding;
                return binding?.Path.Path;
            }
            else if (column is DataGridTemplateColumn templateColumn)
            {
                var binding = templateColumn.ClipboardContentBinding as Binding;
                return binding?.Path.Path;
            }
            return null;
        }

        /// <summary>
        /// get property info from binding path
        /// </summary>
        /// <param name="type"></param>
        /// <param name="propertyPath"></param>
        /// <returns></returns>
        private static PropertyInfo GetPropertyFromPath(Type type, string propertyPath)
        {
            // handle nested property (e.g Person.Age)
            var properties = propertyPath.Split('.');
            PropertyInfo propertyInfo = null;
            Type currentType = type;

            foreach (var property in properties)
            {
                propertyInfo = currentType.GetProperty(property);
                if (propertyInfo == null)
                {
                    return null;
                }

                currentType = propertyInfo.PropertyType;
            }
            return propertyInfo;
        }

        /// <summary>
        /// convert value to type
        /// </summary>
        /// <param name="value"></param>
        /// <param name="targetType"></param>
        /// <returns></returns>
        private static object ConvertValueToPropertyType(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
            {
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            try
            {
                if (targetType == typeof(string))
                {
                    return value;
                }
                else if (targetType == typeof(int) || targetType == typeof(int?))
                {
                    return int.Parse(value);
                }
                else if (targetType == typeof(double) || targetType == typeof(double?))
                {
                    return double.Parse(value);
                }
                else if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                {
                    return decimal.Parse(value);
                }
                else if (targetType == typeof(DateTime) || targetType == typeof(DateTime?))
                {
                    return DateTime.Parse(value);
                }
                else if (targetType == typeof(bool) || targetType == typeof(bool?))
                {
                    return bool.Parse(value);
                }
                else if (targetType.IsEnum)
                {
                    // TODO: ENUM Parsing can cause error, need more graceful handling
                    return Enum.Parse(targetType, value);
                }
                else
                {
                    var converter = TypeDescriptor.GetConverter(targetType);
                    if (converter.CanConvertFrom(typeof(string)))
                    {
                        return converter.ConvertFromString(value);
                    }
                }
            }
            catch
            {
                // TODO: Handle conversion failed
            }
            return null;
        }

        private static void NotifyPropertyChanged(object item, string propertyName)
        {
            if (item is INotifyPropertyChanged notifyPropertyChanged)
            {
                var methodInfo = item.GetType().GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (methodInfo != null)
                {
                    methodInfo.Invoke(item, new[] { propertyName });
                }
            }
        }
    }
}
