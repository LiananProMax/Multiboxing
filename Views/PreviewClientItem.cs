using System.ComponentModel;
using EveOPreview.View;

namespace Multiboxing.Views;

public sealed class PreviewClientItem : INotifyPropertyChanged
{
    private readonly IThumbnailDescription _description;
    private readonly Action<string> _stateChanged;

    internal PreviewClientItem(IThumbnailDescription description, Action<string> stateChanged)
    {
        _description = description;
        _stateChanged = stateChanged;
    }

    internal IThumbnailDescription Description => _description;
    public string Title => _description.Title;

    public bool IsDisabled
    {
        get => _description.IsDisabled;
        set
        {
            if (_description.IsDisabled == value) return;
            _description.IsDisabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDisabled)));
            _stateChanged?.Invoke(Title);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
