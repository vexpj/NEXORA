using System.Collections.ObjectModel;
using NEXORA.App.Mvvm;
using NEXORA.Core.Abstractions;
using NEXORA.Core.Models;

namespace NEXORA.App.ViewModels;

/// <summary>Общий список игровых профилей и выбранный профиль (используется на нескольких страницах).</summary>
public sealed class ProfilesModel : ObservableObject
{
    private readonly IProfileStore _store;
    private GameProfile? _selected;

    public ProfilesModel(IProfileStore store)
    {
        _store = store;
        Items = new ObservableCollection<GameProfile>(store.Load());
        _selected = Items.FirstOrDefault();
    }

    public ObservableCollection<GameProfile> Items { get; }
    public GameProfile? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public void Add(GameProfile p) { Items.Add(p); Selected = p; Save(); }

    public void Remove(GameProfile p)
    {
        Items.Remove(p);
        if (Selected == p || Selected is null) Selected = Items.FirstOrDefault();
        Save();
    }

    public void Save() => _store.Save(Items);
}
