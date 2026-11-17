using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;

namespace SportRentApp
{
    public partial class EquipmentEditor : OverlayWindow
    {
        private readonly Equipment original;
        private string selectedPhoto;
        private bool saving;
        private InputNotice nameNotice, categoryNotice, priceNotice, generalNotice;
        public int SavedId { get; private set; }

        public EquipmentEditor(Equipment record = null)
        {
            InitializeComponent();
            original = record;
            nameNotice = InputNotice.Attach(NameInput);
            categoryNotice = InputNotice.Attach(CategoryInput);
            priceNotice = InputNotice.Attach(PriceInput);
            generalNotice = new InputNotice();
            var grid = (System.Windows.Controls.Grid)ErrorLabel.Parent;
            System.Windows.Controls.Grid.SetRow(generalNotice, 2);
            grid.Children.Add(generalNotice);
            ErrorLabel.Visibility = Visibility.Collapsed;
            if (record != null)
            {
                Heading.Text = "Редагування товару";
                NameInput.Text = record.Name;
                CategoryInput.Text = record.Category;
                PriceInput.Text = record.PricePerDay.ToString("0.##", CultureInfo.GetCultureInfo("uk-UA"));
                DescriptionInput.Text = record.Description;
                PhotoPreview.Source = CatalogService.LoadPhoto(record.ImagePath);
            }

            Loaded += (s, e) => NameInput.Focus();
        }

        private void Photo_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog
            {
                Filter = "Фото (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                CheckFileExists = true
            };
            if (picker.ShowDialog(DialogOwner ?? this) != true)
                return;
            var image = CatalogService.LoadPhoto(picker.FileName);
            if (image == null)
            {
                generalNotice.Show("Не вдалося відкрити фото. Оберіть JPEG або PNG до 10 МБ.");
                return;
            }

            selectedPhoto = picker.FileName;
            PhotoPreview.Source = image;
            PhotoName.Text = Path.GetFileName(selectedPhoto);
            generalNotice.Clear();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (saving)
                return;
            decimal price;
            if (string.IsNullOrWhiteSpace(NameInput.Text))
            {
                nameNotice.Show("Вкажіть назву товару.");
                NameInput.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(CategoryInput.Text))
            {
                categoryNotice.Show("Вкажіть категорію.");
                CategoryInput.Focus();
                return;
            }

            if (!InputRules.TryMoney(PriceInput.Text, out price, true))
            {
                priceNotice.Show("Вкажіть додатну ціну до 1 000 000 грн, не більше двох знаків після коми.");
                PriceInput.Focus();
                return;
            }

            saving = true;
            SaveButton.IsEnabled = CancelButton.IsEnabled = Fields.IsEnabled = false;
            generalNotice.Clear();
            SaveButton.Content = "Зберігаємо…";
            try
            {
                SavedId = await new CatalogService().SaveAsync(new Equipment {
                    Name = NameInput.Text,
                    Category = CategoryInput.Text,
                    Description = DescriptionInput.Text,
                    PricePerDay = (double)price
                }, original, selectedPhoto);
                saving = false;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                generalNotice.Show(ShopForm.Friendly(ex));
            }
            finally
            {
                saving = false;
                SaveButton.Content = "Зберегти товар";
                SaveButton.IsEnabled = CancelButton.IsEnabled = Fields.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (saving)
                e.Cancel = true;
        }
    }
}
