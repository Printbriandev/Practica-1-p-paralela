# Práctica 1: Configuración del Entorno y Hola Mundo Multihilo en C#

Aplicación de consola en C# (.NET 10) que determina la cantidad de procesadores lógicos del equipo con `Environment.ProcessorCount` y lanza tareas en paralelo controlando de forma explícita el grado máximo de paralelismo (`MaxDegreeOfParallelism`).

## Contenido

El programa compara tres estrategias de ejecución sobre la misma carga de trabajo:

1. **Secuencial:** bucle `for` tradicional en el hilo principal.
2. **`Parallel.For`:** con `ParallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount`.
3. **`Task.Run` + `Task.WhenAll`:** limitado con un `SemaphoreSlim` inicializado con `Environment.ProcessorCount`.

Cada fase mide su tiempo con `Stopwatch` y al final se muestra una tabla con el tiempo, el speedup, la concurrencia máxima observada y la cantidad de hilos utilizados.

El grado de paralelismo solo limita cuántas tareas se ejecutan a la vez; el planificador de la Task Parallel Library (TPL) decide qué hilo del ThreadPool ejecuta cada tarea, sin forzar un mapeo estático de un hilo por núcleo.

## Requisitos

- .NET SDK 10.0
- Visual Studio 2026 (o `dotnet` CLI)

## Ejecución

Desde Visual Studio: abrir `HolaMundoMultihilo.slnx` y ejecutar con `Ctrl+F5`.

Desde la terminal:

```bash
dotnet run --project HolaMundoMultihilo
```
