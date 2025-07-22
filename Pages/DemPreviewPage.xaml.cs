using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace DEMBuilder.Pages
{
    public partial class DemPreviewPage : System.Windows.Controls.UserControl
    {
        public event EventHandler? GoBackRequested;
        public event EventHandler? GoToNextPage;

        public DemPreviewPage()
        {
            InitializeComponent();
        }

        public void SetDemImage(BitmapSource image)
        {
            PreviewImage.Source = image;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            GoBackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            GoToNextPage?.Invoke(this, EventArgs.Empty);
        }
    }
}
