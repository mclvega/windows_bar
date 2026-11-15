using System.Collections;
using System.Windows.Controls;
using WindowsBar.Views;

namespace WindowsBar.Controls
{
    public partial class TaskList : UserControl
    {
        public TaskList()
        {
            InitializeComponent();
        }

        public TaskbarWindow Host { get; set; }

        public IEnumerable ItemsSource
        {
            get => Items.ItemsSource;
            set => Items.ItemsSource = value;
        }
    }
}
