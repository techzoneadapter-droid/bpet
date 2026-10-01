using System.Windows;

namespace BPet;

public partial class BubbleWindow : Window
{
    public BubbleWindow() => InitializeComponent();
    public void SetText(string text) => Text.Text = text;
}
