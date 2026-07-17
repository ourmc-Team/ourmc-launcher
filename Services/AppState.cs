using System;
using System.Collections.Generic;
using ourmclauncher.Models;

namespace ourmclauncher.Services;

public class AppState
{
    private int _activeTab;
    public int ActiveTab
    {
        get => _activeTab;
        set { _activeTab = value; NotifyStateChanged(); }
    }

    private int _previousTab;
    public int PreviousTab
    {
        get => _previousTab;
        set { _previousTab = value; NotifyStateChanged(); }
    }

    private bool _isAnimating;
    public bool IsAnimating
    {
        get => _isAnimating;
        set { _isAnimating = value; NotifyStateChanged(); }
    }

    private bool _isLoggedIn;
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        set { _isLoggedIn = value; NotifyStateChanged(); }
    }

    private UserInfo? _currentUser;
    public UserInfo? CurrentUser
    {
        get => _currentUser;
        set { _currentUser = value; NotifyStateChanged(); }
    }

    private string _nickname = "未登录";
    public string Nickname
    {
        get => _nickname;
        set { _nickname = value; NotifyStateChanged(); }
    }

    private string _userEmail = "";
    public string UserEmail
    {
        get => _userEmail;
        set { _userEmail = value; NotifyStateChanged(); }
    }

    private string _avatarUrl = "";
    public string AvatarUrl
    {
        get => _avatarUrl;
        set { _avatarUrl = value; NotifyStateChanged(); }
    }

    public List<GameVersion> Versions { get; } = new();
    
    private bool _isLoadingVersions;
    public bool IsLoadingVersions
    {
        get => _isLoadingVersions;
        set { _isLoadingVersions = value; NotifyStateChanged(); }
    }

    public List<RemoteVersion> RemoteVersions { get; } = new();
    
    private bool _isLoadingRemoteVersions;
    public bool IsLoadingRemoteVersions
    {
        get => _isLoadingRemoteVersions;
        set { _isLoadingRemoteVersions = value; NotifyStateChanged(); }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { _isDownloading = value; NotifyStateChanged(); }
    }

    private int _downloadProgress;
    public int DownloadProgress
    {
        get => _downloadProgress;
        set { _downloadProgress = value; NotifyStateChanged(); }
    }

    private string _downloadStatus = "";
    public string DownloadStatus
    {
        get => _downloadStatus;
        set { _downloadStatus = value; NotifyStateChanged(); }
    }

    private bool _showDownloadSetup;
    public bool ShowDownloadSetup
    {
        get => _showDownloadSetup;
        set { _showDownloadSetup = value; NotifyStateChanged(); }
    }

    private string _installVersionName = "";
    public string InstallVersionName
    {
        get => _installVersionName;
        set { _installVersionName = value; NotifyStateChanged(); }
    }

    private string _installLoaderType = "vanilla";
    public string InstallLoaderType
    {
        get => _installLoaderType;
        set { _installLoaderType = value; NotifyStateChanged(); }
    }

    private string _installLoaderVersion = "";
    public string InstallLoaderVersion
    {
        get => _installLoaderVersion;
        set { _installLoaderVersion = value; NotifyStateChanged(); }
    }

    private string _downloadFilter = "release";
    public string DownloadFilter
    {
        get => _downloadFilter;
        set { _downloadFilter = value; NotifyStateChanged(); }
    }

    private string _javaPath = "";
    public string JavaPath
    {
        get => _javaPath;
        set { _javaPath = value; NotifyStateChanged(); }
    }

    private int _maxMemory = 2048;
    public int MaxMemory
    {
        get => _maxMemory;
        set { _maxMemory = value; NotifyStateChanged(); }
    }

    private string _gameDirectory = "";
    public string GameDirectory
    {
        get => _gameDirectory;
        set { _gameDirectory = value; NotifyStateChanged(); }
    }

    private string _playerName = "Player";
    public string PlayerName
    {
        get => _playerName;
        set { _playerName = value; NotifyStateChanged(); }
    }

    private int _backgroundIndex = 0;
    public int BackgroundIndex
    {
        get => _backgroundIndex;
        set { _backgroundIndex = value; NotifyStateChanged(); }
    }

    private bool _fullscreen = false;
    public bool Fullscreen
    {
        get => _fullscreen;
        set { _fullscreen = value; NotifyStateChanged(); }
    }

    private int _windowWidth = 854;
    public int WindowWidth
    {
        get => _windowWidth;
        set { _windowWidth = value; NotifyStateChanged(); }
    }

    private int _windowHeight = 480;
    public int WindowHeight
    {
        get => _windowHeight;
        set { _windowHeight = value; NotifyStateChanged(); }
    }

    private string _customJvmArgs = "";
    public string CustomJvmArgs
    {
        get => _customJvmArgs;
        set { _customJvmArgs = value; NotifyStateChanged(); }
    }

    private bool _showAccountPopup;
    public bool ShowAccountPopup
    {
        get => _showAccountPopup;
        set { _showAccountPopup = value; NotifyStateChanged(); }
    }

    private string _selectedVersionId = "";
    public string SelectedVersionId
    {
        get => _selectedVersionId;
        set { _selectedVersionId = value; NotifyStateChanged(); }
    }

    public event Action? OnChange;

    private string _aiApiKey = "";
    public string AIApiKey
    {
        get => _aiApiKey;
        set { _aiApiKey = value; NotifyStateChanged(); }
    }

    private bool _showAIWelcome = true;
    public bool ShowAIWelcome
    {
        get => _showAIWelcome;
        set { _showAIWelcome = value; NotifyStateChanged(); }
    }

    public void NotifyStateChanged() => OnChange?.Invoke();

    public void SetLoggedInState(bool loggedIn, UserInfo? user, string avatarUrl)
    {
        IsLoggedIn = loggedIn;
        CurrentUser = user;
        if (user != null)
        {
            Nickname = user.Nickname;
            UserEmail = user.Email;
        }
        else
        {
            Nickname = "未登录";
            UserEmail = "";
        }
        AvatarUrl = avatarUrl;
    }

    public void LoadSettings(AppSettings settings)
    {
        JavaPath = settings.JavaPath;
        MaxMemory = settings.MaxMemory;
        GameDirectory = settings.GameDirectory;
        PlayerName = settings.PlayerName;
        BackgroundIndex = settings.BackgroundIndex;
        Fullscreen = settings.Fullscreen;
        WindowWidth = settings.WindowWidth;
        WindowHeight = settings.WindowHeight;
        CustomJvmArgs = settings.CustomJvmArgs ?? "";
        AIApiKey = settings.AIApiKey ?? "";
        ShowAIWelcome = settings.ShowAIWelcome;
    }
}
