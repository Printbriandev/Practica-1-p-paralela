using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace HolaMundoMultihilo
{
    class Program
    {
        // Contadores compartidos para verificar que nunca se supera el grado máximo de paralelismo
        static int concurrenciaActual;
        static int concurrenciaMaxima;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.WriteLine("=================================================");
            Console.WriteLine(" PRÁCTICA 1: HOLA MUNDO MULTIHILO EN C# (TPL)    ");
            Console.WriteLine("=================================================");

            // 1. Obtención dinámica del número de procesadores lógicos del sistema
            int procesadoresLogicos = Environment.ProcessorCount;
            Console.WriteLine($"\n[INFO] Sistema operativo            : {Environment.OSVersion}");
            Console.WriteLine($"[INFO] Versión de .NET              : {Environment.Version}");
            Console.WriteLine($"[INFO] Procesadores lógicos         : {procesadoresLogicos}");

            // 2. Grado máximo de paralelismo configurado a partir de Environment.ProcessorCount.
            //    Solo se limita cuántas tareas corren a la vez; el planificador de la TPL decide
            //    qué hilo del ThreadPool ejecuta cada una (no hay mapeo fijo hilo-núcleo).
            var opciones = new ParallelOptions
            {
                MaxDegreeOfParallelism = procesadoresLogicos
            };
            Console.WriteLine($"[INFO] MaxDegreeOfParallelism       : {opciones.MaxDegreeOfParallelism}");

            // Se lanzan más tareas que procesadores para que el límite sea observable
            int totalTareas = procesadoresLogicos * 2;
            const int duracionSimuladaMs = 300; // Simulación de carga de trabajo
            Console.WriteLine($"[INFO] Tareas por fase              : {totalTareas}");

            // -----------------------------------------------------------------
            // FASE 1: Ejecución Secuencial (bucle tradicional for)
            // -----------------------------------------------------------------
            ImprimirEncabezado("1. Iniciando Ejecución Secuencial...");

            Stopwatch swSecuencial = Stopwatch.StartNew();
            for (int i = 0; i < totalTareas; i++)
            {
                ProcesarTarea(i, "SECUENCIAL", duracionSimuladaMs);
            }
            swSecuencial.Stop();
            Console.WriteLine($"[RESULTADO] Tiempo total secuencial: {swSecuencial.ElapsedMilliseconds} ms");

            // -----------------------------------------------------------------
            // FASE 2: Parallel.For con MaxDegreeOfParallelism explícito
            // -----------------------------------------------------------------
            ImprimirEncabezado("2. Iniciando Ejecución Paralela con Parallel.For...");
            ReiniciarContadores();

            var hilosParallelFor = new ConcurrentDictionary<int, int>();
            Stopwatch swParallelFor = Stopwatch.StartNew();
            Parallel.For(0, totalTareas, opciones, index =>
            {
                hilosParallelFor.AddOrUpdate(Environment.CurrentManagedThreadId, 1, (_, n) => n + 1);
                ProcesarTarea(index, "PARALLEL.FOR", duracionSimuladaMs);
            });
            swParallelFor.Stop();
            int maxParallelFor = concurrenciaMaxima;
            Console.WriteLine($"[RESULTADO] Tiempo total con Parallel.For: {swParallelFor.ElapsedMilliseconds} ms");

            // -----------------------------------------------------------------
            // FASE 3: Task.Run + Task.WhenAll limitado con SemaphoreSlim
            // -----------------------------------------------------------------
            // Task.Run no acepta ParallelOptions, así que el mismo límite se aplica con un
            // SemaphoreSlim inicializado con Environment.ProcessorCount.
            ImprimirEncabezado("3. Iniciando Ejecución Asíncrona con Task.Run...");
            ReiniciarContadores();

            var hilosTaskRun = new ConcurrentDictionary<int, int>();
            using var limitador = new SemaphoreSlim(opciones.MaxDegreeOfParallelism);
            Stopwatch swTaskRun = Stopwatch.StartNew();
            Task[] arregloTareas = new Task[totalTareas];

            for (int i = 0; i < totalTareas; i++)
            {
                int taskId = i; // Captura local para evitar problemas de cierre (closure)
                arregloTareas[i] = Task.Run(async () =>
                {
                    await limitador.WaitAsync();
                    try
                    {
                        hilosTaskRun.AddOrUpdate(Environment.CurrentManagedThreadId, 1, (_, n) => n + 1);
                        ProcesarTarea(taskId, "TASK.RUN", duracionSimuladaMs);
                    }
                    finally
                    {
                        limitador.Release();
                    }
                });
            }

            // Espera la finalización de todas las tareas iniciadas (punto de unión / join)
            await Task.WhenAll(arregloTareas);
            swTaskRun.Stop();
            int maxTaskRun = concurrenciaMaxima;
            Console.WriteLine($"[RESULTADO] Tiempo total con Task.Run: {swTaskRun.ElapsedMilliseconds} ms");

            // -----------------------------------------------------------------
            // RESUMEN DE COMPARACIÓN Y RENDIMIENTO
            // -----------------------------------------------------------------
            double speedupFor = (double)swSecuencial.ElapsedMilliseconds / swParallelFor.ElapsedMilliseconds;
            double speedupTask = (double)swSecuencial.ElapsedMilliseconds / swTaskRun.ElapsedMilliseconds;

            Console.WriteLine("\n=================================================");
            Console.WriteLine(" RESUMEN DE TIEMPOS DE EJECUCIÓN ");
            Console.WriteLine("=================================================");
            Console.WriteLine($"Procesadores lógicos    : {procesadoresLogicos}");
            Console.WriteLine($"MaxDegreeOfParallelism  : {opciones.MaxDegreeOfParallelism}");
            Console.WriteLine("-------------------------------------------------");
            Console.WriteLine("Estrategia    | Tiempo (ms) | Speedup | Conc. máx | Hilos");
            Console.WriteLine($"Secuencial    | {swSecuencial.ElapsedMilliseconds,11} | {1.0,6:F2}x | {1,9} | {1,5}");
            Console.WriteLine($"Parallel.For  | {swParallelFor.ElapsedMilliseconds,11} | {speedupFor,6:F2}x | {maxParallelFor,9} | {hilosParallelFor.Count,5}");
            Console.WriteLine($"Task.Run      | {swTaskRun.ElapsedMilliseconds,11} | {speedupTask,6:F2}x | {maxTaskRun,9} | {hilosTaskRun.Count,5}");
            Console.WriteLine("=================================================");

            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("\nPresione cualquier tecla para salir...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Simula el procesamiento de un trabajo y registra cuántas tareas se ejecutan a la vez.
        /// </summary>
        static void ProcesarTarea(int id, string origen, int duracionMs)
        {
            int enEjecucion = Interlocked.Increment(ref concurrenciaActual);
            ActualizarMaximo(enEjecucion);

            Thread hilo = Thread.CurrentThread;
            Console.WriteLine($"[{origen}] Hola Mundo desde la tarea {id,2} | Hilo #{hilo.ManagedThreadId,2} " +
                              $"| ThreadPool: {hilo.IsThreadPoolThread,-5} | Simultáneas: {enEjecucion}");
            Thread.Sleep(duracionMs);

            Interlocked.Decrement(ref concurrenciaActual);
        }

        static void ActualizarMaximo(int valor)
        {
            int actual;
            while (valor > (actual = Volatile.Read(ref concurrenciaMaxima)))
            {
                if (Interlocked.CompareExchange(ref concurrenciaMaxima, valor, actual) == actual)
                {
                    break;
                }
            }
        }

        static void ReiniciarContadores()
        {
            concurrenciaActual = 0;
            concurrenciaMaxima = 0;
        }

        static void ImprimirEncabezado(string titulo)
        {
            Console.WriteLine("\n-------------------------------------------------");
            Console.WriteLine($" {titulo}");
            Console.WriteLine("-------------------------------------------------");
        }
    }
}
