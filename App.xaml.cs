using System.Configuration;
using System.Data;
using System.Windows;


namespace DEMBuilder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            // GdalBase.ConfigureAll();

            base.OnStartup(e);
        }
    }

}
