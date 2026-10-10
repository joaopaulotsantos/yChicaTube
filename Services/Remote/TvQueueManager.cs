using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Microsoft.Maui.Storage;
using yChicaTube.Services.Remote.Models;

namespace yChicaTube.Services.Remote;

public class TvQueueManager
{
    private static readonly Lazy<TvQueueManager> _instance = new(() => new TvQueueManager());
    public static TvQueueManager Instance => _instance.Value;

    private const string TvQueueStorageKey = "tv_active_playback_queue_v1";
    private readonly object _lock = new();

    public ObservableCollection<TvVideoItem> Queue { get; } = new();
    public int CurrentIndex { get; private set; } = -1;

    public TvVideoItem? CurrentVideo
    {
        get
        {
            lock (_lock)
            {
                if (CurrentIndex >= 0 && CurrentIndex < Queue.Count)
                    return Queue[CurrentIndex];
                return null;
            }
        }
    }

    public event Action? QueueChanged;
    public event Action<TvVideoItem?>? CurrentVideoChanged;

    public TvQueueManager()
    {
        LoadSavedQueue();
    }

    public void SetQueue(IEnumerable<TvVideoItem> videos, int startIndex = 0)
    {
        lock (_lock)
        {
            Queue.Clear();
            var pos = 1;
            foreach (var v in videos)
            {
                v.Position = pos++;
                Queue.Add(v);
            }

            CurrentIndex = (Queue.Count > 0 && startIndex >= 0 && startIndex < Queue.Count) ? startIndex : (Queue.Count > 0 ? 0 : -1);
            SaveQueue();
        }

        QueueChanged?.Invoke();
        CurrentVideoChanged?.Invoke(CurrentVideo);
    }

    public void PlayVideoImmediately(TvVideoItem video)
    {
        lock (_lock)
        {
            // Se já existir na fila, move para a posição atual ou seleciona
            var existingIndex = Queue.ToList().FindIndex(v => v.VideoId == video.VideoId);
            if (existingIndex >= 0)
            {
                CurrentIndex = existingIndex;
            }
            else
            {
                // Insere na posição seguinte à atual
                var insertIndex = CurrentIndex >= 0 ? Math.Min(CurrentIndex + 1, Queue.Count) : 0;
                video.Position = insertIndex + 1;
                Queue.Insert(insertIndex, video);
                CurrentIndex = insertIndex;
                UpdatePositions();
            }
            SaveQueue();
        }

        QueueChanged?.Invoke();
        CurrentVideoChanged?.Invoke(CurrentVideo);
    }

    public void Enqueue(TvVideoItem video)
    {
        lock (_lock)
        {
            // Evita duplicados contíguos desnecessários
            if (!Queue.Any(v => v.VideoId == video.VideoId))
            {
                video.Position = Queue.Count + 1;
                Queue.Add(video);
                if (CurrentIndex == -1 && Queue.Count > 0)
                {
                    CurrentIndex = 0;
                }
                SaveQueue();
            }
        }

        QueueChanged?.Invoke();
    }

    public void EnqueueRange(IEnumerable<TvVideoItem> videos)
    {
        lock (_lock)
        {
            foreach (var v in videos)
            {
                if (!Queue.Any(existing => existing.VideoId == v.VideoId))
                {
                    v.Position = Queue.Count + 1;
                    Queue.Add(v);
                }
            }

            if (CurrentIndex == -1 && Queue.Count > 0)
            {
                CurrentIndex = 0;
            }
            SaveQueue();
        }

        QueueChanged?.Invoke();
    }

    public bool RemoveById(string videoId)
    {
        lock (_lock)
        {
            var idx = Queue.ToList().FindIndex(v => v.VideoId == videoId);
            if (idx >= 0)
            {
                Queue.RemoveAt(idx);
                if (CurrentIndex >= Queue.Count)
                {
                    CurrentIndex = Queue.Count - 1;
                }
                UpdatePositions();
                SaveQueue();
                QueueChanged?.Invoke();
                return true;
            }
            return false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Queue.Clear();
            CurrentIndex = -1;
            SaveQueue();
        }

        QueueChanged?.Invoke();
        CurrentVideoChanged?.Invoke(null);
    }

    public TvVideoItem? Next()
    {
        lock (_lock)
        {
            if (CurrentIndex + 1 < Queue.Count)
            {
                CurrentIndex++;
                SaveQueue();
                CurrentVideoChanged?.Invoke(CurrentVideo);
                return CurrentVideo;
            }
            return null;
        }
    }

    public TvVideoItem? Previous()
    {
        lock (_lock)
        {
            if (CurrentIndex > 0)
            {
                CurrentIndex--;
                SaveQueue();
                CurrentVideoChanged?.Invoke(CurrentVideo);
                return CurrentVideo;
            }
            return null;
        }
    }

    public bool HasNext => CurrentIndex + 1 < Queue.Count;
    public bool HasPrevious => CurrentIndex > 0;

    private void UpdatePositions()
    {
        for (int i = 0; i < Queue.Count; i++)
        {
            Queue[i].Position = i + 1;
        }
    }

    private void SaveQueue()
    {
        try
        {
            var list = Queue.ToList();
            var json = JsonSerializer.Serialize(list);
            Preferences.Default.Set(TvQueueStorageKey, json);
        }
        catch { }
    }

    private void LoadSavedQueue()
    {
        try
        {
            var json = Preferences.Default.Get(TvQueueStorageKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonSerializer.Deserialize<List<TvVideoItem>>(json);
                if (list != null && list.Count > 0)
                {
                    Queue.Clear();
                    foreach (var item in list)
                    {
                        Queue.Add(item);
                    }
                    CurrentIndex = 0;
                }
            }
        }
        catch { }
    }
}
