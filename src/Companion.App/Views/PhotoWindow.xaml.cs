using System.Windows;
using System.Windows.Media;

namespace Companion.App.Views;

/// <summary>A resizable full-size photo viewer: the image fills the window and scales with it
/// (Stretch=Uniform), so dragging the window edges resizes the photo. Opened from the Calendar tab's
/// venue photo.</summary>
public partial class PhotoWindow : Window
{
    public PhotoWindow(ImageSource source, string title)
    {
        InitializeComponent();
        Photo.Source = source;
        Title = string.IsNullOrWhiteSpace(title) ? "Photo" : title;
    }
}
