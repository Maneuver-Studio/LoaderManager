using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Maneuver.Loader
{
    public class LoaderManager
    {
        public IBackgroundTaskQueue TaskQueue => new BackgroundTaskQueue();
        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (false == stoppingToken.IsCancellationRequested)
            {
                var workItem = await TaskQueue.DequeueAsync(stoppingToken);
                try
                {
                    await workItem(stoppingToken);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error occurred executing {nameof(workItem)}: {ex}");
                }
            }
        }
    }
}
