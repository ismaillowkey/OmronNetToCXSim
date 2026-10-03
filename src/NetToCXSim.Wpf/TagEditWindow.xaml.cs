using System;
using System.Windows;
using System.Windows.Controls;
using NetToCXSim.Services;

namespace NetToCXSim
{
    public partial class TagEditWindow : Window
    {
        public OpcTagItem TagItem { get; private set; }

        public TagEditWindow(OpcTagItem existingTag = null)
        {
            InitializeComponent();

            if (existingTag != null)
            {
                TagItem = existingTag.Clone();
                TxtHeaderTitle.Text = "EDIT OPC TAG";
                TxtTagName.Text = TagItem.TagName;
                TxtAddress.Text = TagItem.Address;
                TxtDescription.Text = TagItem.Description;
                ChkActive.IsChecked = TagItem.IsActive;

                foreach (ComboBoxItem item in CmbDataType.Items)
                {
                    if (item.Content.ToString().Equals(TagItem.DataType.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        CmbDataType.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                TagItem = new OpcTagItem();
                TxtHeaderTitle.Text = "ADD NEW OPC TAG";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtTagName.Text?.Trim();
            string addr = TxtAddress.Text?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Tag Name cannot be empty!", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTagName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(addr))
            {
                MessageBox.Show("CX-Simulator address cannot be empty!", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAddress.Focus();
                return;
            }

            if (!OmronSimulatorEngine.TryParseAddress(addr, out _, out _, out _))
            {
                var r = MessageBox.Show($"Address format '{addr}' may not be valid for Omron CX-Simulator.\nSave anyway?", "Confirm Address", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            TagItem.TagName = name;
            TagItem.Address = addr;
            TagItem.Description = TxtDescription.Text?.Trim() ?? "";
            TagItem.IsActive = ChkActive.IsChecked ?? true;

            string selectedDt = (CmbDataType.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Bool";
            if (Enum.TryParse(selectedDt, true, out OpcDataType dt))
            {
                TagItem.DataType = dt;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
