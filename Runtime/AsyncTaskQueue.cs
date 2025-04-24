using Cysharp.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Maneuver.LoaderManager
{
    /// <summary>
    /// Enum para definir o resultado de uma tarefa.
    /// </summary>
    public enum TaskLoaderResult
    {
        Failed,
        Success
    }

    public class AsyncTaskQueue
    {
        // Fila que armazena funções que retornam Task<TaskResult>
        private readonly ConcurrentQueue<Func<UniTask<TaskLoaderResult>>> _taskQueue = new ConcurrentQueue<Func<UniTask<TaskLoaderResult>>>();

        // Token de cancelamento utilizado para interromper o processamento da fila
        private CancellationTokenSource _cancellationTokenSource;

        // Tarefa responsável pelo processamento da fila
        private UniTask _processingTask;

        // Indica se a fila está em execução
        public bool IsRunning { get; private set; } = false;

        private readonly object _lockObj = new object();

        // Contador de tarefas processadas com sucesso
        private int _tasksProcessed = 0;

        /// <summary>
        /// Evento disparado sempre que o progresso da fila é atualizado.
        /// O primeiro parâmetro representa o número de tarefas processadas e o segundo, as tarefas restantes.
        /// </summary>
        public event Action<int, int> OnProgressChanged;


        /// <summary>
        /// Evento que é disparado sempre que a fila falha. 
        /// O primeiro parâmetro é uma mensagem descritiva e o segundo, a exceção (se houver).
        /// </summary>
        public event Action<Exception> OnQueueFailed;

        /// <summary>
        /// Adiciona uma nova tarefa à fila.
        /// A função deve retornar um Task<TaskResult> indicando se a tarefa teve sucesso ou falhou.
        /// </summary>
        /// <param name="taskFunc">Função assíncrona que representa a tarefa.</param>
        public void EnqueueTask(Func<UniTask<TaskLoaderResult>> taskFunc)
        {
            if (taskFunc == null)
                throw new ArgumentNullException(nameof(taskFunc));

            _taskQueue.Enqueue(taskFunc);

            OnProgressChanged?.Invoke(_tasksProcessed, _taskQueue.Count);
        }

        /// <summary>
        /// Inicia o processamento da fila de tarefas.
        /// </summary>
        /// <exception cref="InvalidOperationException">Lançada se a fila já estiver em execução.</exception>
        public void StartQueue()
        {
            lock (_lockObj)
            {
                if (IsRunning)
                    throw new InvalidOperationException("A fila já está em execução.");

                _cancellationTokenSource = new CancellationTokenSource();
                IsRunning = true;
                _processingTask = ProcessQueueAsync(_cancellationTokenSource.Token);
            }
        }

        /// <summary>
        /// Processa as tarefas enfileiradas.
        /// Caso uma tarefa retorne Failed ou lance uma exceção, dispara o evento OnQueueFailed e interrompe o processamento.
        /// </summary>
        /// <param name="token">Token de cancelamento.</param>
        private async UniTask ProcessQueueAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (_taskQueue.TryDequeue(out var taskFunc))
                    {
                        try
                        {
                            // Executa a tarefa e aguarda seu resultado
                            TaskLoaderResult result = await taskFunc();
                            if (result == TaskLoaderResult.Failed)
                            {
                                OnQueueFailed?.Invoke(null);
                                _cancellationTokenSource.Cancel();
                                break;
                            }

                            _tasksProcessed++;
                            OnProgressChanged?.Invoke(_tasksProcessed, _taskQueue.Count);
                        }
                        catch (Exception ex)
                        {
                            OnQueueFailed?.Invoke(ex);
                            _cancellationTokenSource.Cancel();
                            break;
                        }
                    }
                    else
                    {
                        // Aguarda um curto intervalo para evitar um loop intenso caso não haja tarefas
                        await Task.Delay(100, token);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Exceção esperada quando o token é cancelado
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Interrompe o processamento da fila e aguarda a finalização da tarefa em background.
        /// </summary>
        public async UniTask StopQueueAsync()
        {
            if (!IsRunning)
                return;

            _cancellationTokenSource.Cancel();
            try
            {
                await _processingTask;
            }
            catch (TaskCanceledException)
            {
                // Exceção esperada, pode ser ignorada
            }
            finally
            {
                IsRunning = false;
            }
        }
    }
}
