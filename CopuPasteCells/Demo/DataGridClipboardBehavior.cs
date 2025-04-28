using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Expression = System.Linq.Expressions.Expression;


 namespace Demo
{
    public class DataGridClipboardBehavior
    {
        #region Dependency Property Registration

        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(DataGridClipboardBehavior),
                new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        #endregion

        #region Caches

        // Cache for column property paths
        private static readonly ConcurrentDictionary<DataGridColumn, string> ColumnPathCache =
            new ConcurrentDictionary<DataGridColumn, string>();

        // Cache for property info objects
        private static readonly ConcurrentDictionary<string, PropertyInfo> PropertyCache =
            new ConcurrentDictionary<string, PropertyInfo>();

        // Cache for type converters
        private static readonly ConcurrentDictionary<Type, TypeConverter> TypeConverterCache =
            new ConcurrentDictionary<Type, TypeConverter>();

        // Cache for property getters and setters
        private static readonly ConcurrentDictionary<string, Func<object, object>> PropertyGetterCache =
            new ConcurrentDictionary<string, Func<object, object>>();
        private static readonly ConcurrentDictionary<string, Action<object, object>> PropertySetterCache =
            new ConcurrentDictionary<string, Action<object, object>>();

        // Cache for OnPropertyChanged methods
        private static readonly ConcurrentDictionary<Type, MethodInfo> PropertyChangedMethodCache =
            new ConcurrentDictionary<Type, MethodInfo>();

        #endregion

        #region Event Handling

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is DataGrid dataGrid)) return;

            if ((bool)e.NewValue)
            {
                // Add command bindings when enabled
                dataGrid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
                    OnPasteExecuted, OnPasteCanExecute));
                dataGrid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy,
                    OnCopyExecuted, OnCopyCanExecute));
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

        private static void OnCopyCanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = sender is DataGrid grid && grid.SelectedCells.Count > 0;
        }

        private static void OnCopyExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (sender is DataGrid dataGrid)
            {
                CopyDataGridSelectionToClipboard(dataGrid);
            }
        }

        #endregion

        #region Copy Implementation

        private static void CopyDataGridSelectionToClipboard(DataGrid dataGrid)
        {
            var selectedCells = dataGrid.SelectedCells;
            if (selectedCells.Count == 0) return;

            // Group cells by row
            var cellsByRow = selectedCells.GroupBy(cell => dataGrid.Items.IndexOf(cell.Item))
                                         .OrderBy(group => group.Key)
                                         .ToList();

            var clipboardText = new StringBuilder();

            foreach (var rowGroup in cellsByRow)
            {
                bool isFirstCell = true;

                // Sort cells by column index within each row
                foreach (var cell in rowGroup.OrderBy(c => dataGrid.Columns.IndexOf(c.Column)))
                {
                    if (!isFirstCell)
                        clipboardText.Append('\t');

                    string value = GetCellValueFast(cell.Item, cell.Column);
                    clipboardText.Append(value ?? string.Empty);
                    isFirstCell = false;
                }

                clipboardText.AppendLine();
            }

            Clipboard.SetText(clipboardText.ToString());
        }

        private static string GetCellValueFast(object item, DataGridColumn column)
        {
            string propertyPath = GetColumnPropertyPathCached(column);
            if (string.IsNullOrEmpty(propertyPath)) return null;

            try
            {
                var value = GetPropertyValueFast(item, item.GetType(), propertyPath);
                return value?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region Paste Implementation

        private static void PasteClipboardToDataGrid(DataGrid dataGrid)
        {
            // Get clipboard data
            var clipboardText = Clipboard.GetText();
            if (string.IsNullOrEmpty(clipboardText)) return;

            var clipboardLines = clipboardText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (clipboardLines.Length == 0) return;

            // Get current cell position
            var currentCell = dataGrid.CurrentCell;
            if (currentCell.Column == null || currentCell.Item == null) return;

            var startRowIndex = dataGrid.Items.IndexOf(currentCell.Item);
            var startColumnIndex = dataGrid.Columns.IndexOf(currentCell.Column);

            if (startRowIndex < 0 || startColumnIndex < 0) return;
            try
            {
                // Process each line (rows)
                for (int i = 0; i < clipboardLines.Length; i++)
                {
                    var rowIndex = startRowIndex + i;
                    if (rowIndex >= dataGrid.Items.Count) break;

                    var currentItem = dataGrid.Items[rowIndex];
                    var values = clipboardLines[i].Split('\t');

                    // Process values in line (columns)
                    for (int j = 0; j < values.Length; j++)
                    {
                        var columnIndex = startColumnIndex + j;
                        if (columnIndex >= dataGrid.Columns.Count) break;

                        var column = dataGrid.Columns[columnIndex];
                        SetCellValueFast(currentItem, column, values[j]);
                    }
                }
            }
            catch
            {
                // Handle exceptions silently to avoid crashing the UI
                // This could be due to invalid data types or other issues
            }
        }

        private static void SetCellValueFast(object item, DataGridColumn column, string value)
        {
            // Get property path
            string propertyPath = GetColumnPropertyPathCached(column);
            if (string.IsNullOrEmpty(propertyPath)) return;

            Type itemType = item.GetType();

            try
            {
                // Use cached or create setter
                SetPropertyValueFast(item, itemType, propertyPath, value);

                // Notify property changed
                NotifyPropertyChangedFast(item, propertyPath);
            }
            catch
            {
                // Silently ignore errors - we don't want to crash the UI
            }
        }

        #endregion

        #region Reflection Optimization Methods

        private static string GetColumnPropertyPathCached(DataGridColumn column)
        {
            // Try to get from cache first
            if (ColumnPathCache.TryGetValue(column, out string path))
                return path;

            // Cache miss - get property path
            string propertyPath = null;

            if (column is DataGridBoundColumn boundColumn)
            {
                var binding = boundColumn.Binding as Binding;
                propertyPath = binding?.Path.Path;
            }
            else if (column is DataGridTemplateColumn templateColumn)
            {
                var binding = templateColumn.ClipboardContentBinding as Binding;
                propertyPath = binding?.Path.Path;
            }

            // Store in cache if found
            if (propertyPath != null)
                ColumnPathCache[column] = propertyPath;

            return propertyPath;
        }

        private static PropertyInfo GetPropertyFromPathCached(Type type, string propertyPath)
        {
            // Create a cache key
            string cacheKey = $"{type.FullName}|{propertyPath}";

            // Try to get from cache first
            if (PropertyCache.TryGetValue(cacheKey, out PropertyInfo cachedProperty))
                return cachedProperty;

            // Cache miss - split and find property
            var properties = propertyPath.Split('.');
            PropertyInfo propertyInfo = null;
            Type currentType = type;

            foreach (var property in properties)
            {
                propertyInfo = currentType.GetProperty(property);
                if (propertyInfo == null) return null;

                currentType = propertyInfo.PropertyType;
            }

            // Store in cache if found
            if (propertyInfo != null)
                PropertyCache[cacheKey] = propertyInfo;

            return propertyInfo;
        }

        private static object GetPropertyValueFast(object target, Type targetType, string propertyPath)
        {
            string cacheKey = $"{targetType.FullName}|{propertyPath}";

            var getter = PropertyGetterCache.GetOrAdd(cacheKey, _ =>
            {
                // Create parameter expression for the target object
                ParameterExpression param = Expression.Parameter(typeof(object));

                // Cast parameter to the target type
                UnaryExpression castParam = Expression.Convert(param, targetType);

                // Build property access expression
                Expression propertyAccess = castParam;
                foreach (var propName in propertyPath.Split('.'))
                {
                    propertyAccess = Expression.Property(propertyAccess, propName);
                }

                // Convert result back to object
                UnaryExpression convertResult = Expression.Convert(propertyAccess, typeof(object));

                // Create and compile lambda expression
                var lambda = Expression.Lambda<Func<object, object>>(convertResult, param);
                return lambda.Compile();
            });

            try
            {
                return getter(target);
            }
            catch
            {
                return null;
            }
        }

        private static void SetPropertyValueFast(object target, Type targetType, string propertyPath, string stringValue)
        {
            // First, get property info to determine type
            PropertyInfo propertyInfo = GetPropertyFromPathCached(targetType, propertyPath);
            if (propertyInfo == null) return;

            // Convert the string value to the target property type
            object convertedValue = ConvertValueToPropertyTypeOptimized(stringValue, propertyInfo.PropertyType);

            // Get or create the setter
            string cacheKey = $"{targetType.FullName}|{propertyPath}";
            var setter = PropertySetterCache.GetOrAdd(cacheKey, _ =>
            {
                // Get the target property and its type
                PropertyInfo propInfo = GetPropertyFromPathCached(targetType, propertyPath);
                if (propInfo == null) return null;

                // Create parameters for the lambda
                ParameterExpression targetParam = Expression.Parameter(typeof(object), "target");
                ParameterExpression valueParam = Expression.Parameter(typeof(object), "value");

                // Cast target to specific type
                UnaryExpression castTarget = Expression.Convert(targetParam, targetType);

                // Build property access chain for nested properties
                Expression propertyAccess = castTarget;
                string[] propertyParts = propertyPath.Split('.');

                for (int i = 0; i < propertyParts.Length - 1; i++)
                {
                    propertyAccess = Expression.Property(propertyAccess, propertyParts[i]);
                }

                // Get the final property
                string finalPropertyName = propertyParts[propertyParts.Length - 1];
                PropertyInfo finalProperty = propertyAccess.Type.GetProperty(finalPropertyName);

                // Create property access expression
                MemberExpression property = Expression.Property(propertyAccess, finalProperty);

                // Cast value to property type
                UnaryExpression castValue = Expression.Convert(valueParam, finalProperty.PropertyType);

                // Create assignment
                BinaryExpression assign = Expression.Assign(property, castValue);

                // Create and compile the lambda
                var lambda = Expression.Lambda<Action<object, object>>(assign, targetParam, valueParam);
                return lambda.Compile();
            });

            // Use the compiled setter
            if (setter != null)
            {
                try
                {
                    setter(target, convertedValue);
                }
                catch
                {
                    // Fallback to regular reflection if setter fails
                    propertyInfo.SetValue(target, convertedValue);
                }
            }
        }

        private static object ConvertValueToPropertyTypeOptimized(string value, Type targetType)
        {
            // Handle null or empty values
            if (string.IsNullOrEmpty(value))
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            try
            {
                // Fast path for common types (no boxing for value types)
                if (targetType == typeof(string))
                    return value;
                else if (targetType == typeof(int))
                    return int.Parse(value);
                else if (targetType == typeof(int?))
                    return int.Parse(value); // Will be boxed anyway for nullable
                else if (targetType == typeof(double))
                    return double.Parse(value);
                else if (targetType == typeof(double?))
                    return double.Parse(value);
                else if (targetType == typeof(decimal))
                    return decimal.Parse(value);
                else if (targetType == typeof(decimal?))
                    return decimal.Parse(value);
                else if (targetType == typeof(DateTime))
                    return DateTime.Parse(value);
                else if (targetType == typeof(DateTime?))
                    return DateTime.Parse(value);
                else if (targetType == typeof(bool))
                    return bool.Parse(value);
                else if (targetType == typeof(bool?))
                    return bool.Parse(value);
                else if (targetType.IsEnum)
                {
                    // Try case-insensitive parsing for better usability
                    return Enum.Parse(targetType, value, true);
                }
                else
                {
                    // Use cached TypeConverter as fallback
                    var converter = TypeConverterCache.GetOrAdd(targetType, t => TypeDescriptor.GetConverter(t));
                    if (converter.CanConvertFrom(typeof(string)))
                        return converter.ConvertFromString(value);
                }
            }
            catch
            {
                // Return default value on conversion failure
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            }

            return null;
        }

        private static void NotifyPropertyChangedFast(object item, string propertyName)
        {
            if (!(item is INotifyPropertyChanged)) return;

            Type itemType = item.GetType();

            var methodInfo = PropertyChangedMethodCache.GetOrAdd(itemType, t =>
                t.GetMethod("OnPropertyChanged",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

            if (methodInfo != null)
            {
                try
                {
                    methodInfo.Invoke(item, new[] { propertyName });
                }
                catch
                {
                    // Ignore reflection errors during notification
                }
            }
        }

        #endregion
    }
}
