# Implementation of Copy & Paste Action in WPF DataGrid

To enable copy and paste functionality in your WPF `DataGrid`, you need to use the `DataGridClipboardBehavior`.

## Usage

In your `DataGrid`, add the following:

```xaml
<DataGrid 
    local:DataGridClipboardBehavior.Enabled="True"
    ItemsSource="{Binding bindlist, UpdateSourceTrigger=PropertyChanged}"
    SelectionUnit="Cell">
</DataGrid>
