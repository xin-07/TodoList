using System;
using System.Windows.Input;
using TodoList.Models;
using TodoList.Repositories;

namespace TodoList.ViewModels;

/// <summary>左侧边栏视图种类：全部任务 / 具体文件夹 / 未归类。</summary>
public enum SidebarKind
{
    /// <summary>全部任务（含所有文件夹与未归类）。</summary>
    All,

    /// <summary>某个具体文件夹。</summary>
    Folder,

    /// <summary>未归类任务（FolderId 为 null）。</summary>
    Uncategorized,
}

/// <summary>
/// 左侧边栏单个展示项，覆盖三种视图（<see cref="SidebarKind"/>）。
/// 仅当 <see cref="Kind"/> 为 Folder 时才包装 <see cref="MyFolder"/>。
/// 提供选中态、内置条目数与原地重命名；不持有权威数据。
/// </summary>
public sealed class SidebarItemViewModel : ViewModelBase
{
    private readonly ITodoRepository _repo;
    private bool _isRenaming;
    private string _editText = "";
    private string _editError = "";
    private int _itemCount;

    public SidebarItemViewModel(ITodoRepository repo, SidebarKind kind, MyFolder? folder = null)
    {
        _repo = repo;
        Kind = kind;
        Folder = folder;

        _editText = Name;

        BeginRenameCommand = new RelayCommand(_ => BeginRename());
        SaveRenameCommand = new RelayCommand(_ => SaveRename());
        CancelRenameCommand = new RelayCommand(_ => CancelRename());
    }

    public SidebarKind Kind { get; }

    /// <summary>当 Kind 为 Folder 时为被包装的仓库投影对象；否则为 null。</summary>
    public MyFolder? Folder { get; }

    /// <summary>该项的稳定标识：全部="all"、未归类="none"、文件夹=其 Id。</summary>
    public string Key => Kind switch
    {
        SidebarKind.Folder => Folder?.Id ?? "",
        SidebarKind.Uncategorized => "none",
        _ => "all",
    };

    /// <summary>边栏展示名称。</summary>
    public string Name => Kind switch
    {
        SidebarKind.Folder => Folder?.Name ?? "",
        SidebarKind.Uncategorized => "未归类",
        _ => "全部任务",
    };

    /// <summary>是否为具体文件夹视图（决定是否展示重命名/删除操作）。</summary>
    public bool IsFolder => Kind == SidebarKind.Folder;

    public int ItemCount
    {
        get => _itemCount;
        set => SetProperty(ref _itemCount, value);
    }

    // ---- 原地重命名 ----

    public bool IsRenaming { get => _isRenaming; private set => SetProperty(ref _isRenaming, value); }

    public bool IsNotRenaming => !_isRenaming;

    public string EditText
    {
        get => _editText;
        set
        {
            if (SetProperty(ref _editText, value) && !string.IsNullOrWhiteSpace(value))
                EditError = "";
        }
    }

    public string EditError { get => _editError; private set => SetProperty(ref _editError, value); }

    public ICommand BeginRenameCommand { get; }
    public ICommand SaveRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }

    private void BeginRename()
    {
        if (Kind != SidebarKind.Folder || Folder is null)
            return;
        _editText = Folder.Name;
        EditError = "";
        IsRenaming = true;
        OnPropertyChanged(nameof(IsNotRenaming));
    }

    private void SaveRename()
    {
        if (!_isRenaming)
            return;

        var name = _editText?.Trim() ?? "";
        var error = FolderName.Validate(name);
        if (error is not null)
        {
            EditError = error;
            return;
        }

        IsRenaming = false;
        OnPropertyChanged(nameof(IsNotRenaming));

        if (Folder is not null && !string.Equals(name, Folder.Name, StringComparison.Ordinal))
            _repo.RenameFolder(Folder.Id, name);
    }

    private void CancelRename()
    {
        if (!_isRenaming)
            return;
        if (Folder is not null)
            _editText = Folder.Name;
        EditError = "";
        IsRenaming = false;
        OnPropertyChanged(nameof(IsNotRenaming));
    }
}