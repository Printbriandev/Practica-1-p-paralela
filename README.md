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

---

# Práctica de Laboratorio 1: Ley de Amdahl y Análisis de Rendimiento Concurrente

Carpeta: [`LeyAmdahlPedidos`](LeyAmdahlPedidos/ProcesamientoPedidos.cs)

Procesamiento de 30,000 pedidos de comercio electrónico, cada uno con un cálculo matemático intensivo, comparando la versión secuencial con versiones paralelas:

1. **Secuencial:** bucle `for` original.
2. **`Parallel.For` sin `lock`:** incorrecta a propósito, para demostrar la condición de carrera sobre la variable compartida `total`.
3. **`Parallel.For` con `lock` en cada pedido:** probada con `MaxDegreeOfParallelism` de 1, 2, 4, 8 y 16.
4. **`Parallel.For` con acumuladores locales:** `localInit` / `localFinally`, con un solo `lock` por hilo.

Para cada grado de paralelismo se calcula el Speedup `S = T_secuencial / T_paralelo` y la Eficiencia `E = S / p`. El análisis (Ley de Amdahl, Ley de Gustafson y la pregunta general) está al final del archivo, en el comentario `/* Mi análisis ... */`.

```bash
dotnet run -c Release --project LeyAmdahlPedidos
```

---

# Práctica de Laboratorio 2: Ley de Gustafson y Análisis de Rendimiento Concurrente

Carpeta: [`LeyGustafsonPedidos`](LeyGustafsonPedidos/LeyGustafsonPedidos.cs)

En lugar de terminar el mismo trabajo más rápido, se busca procesar más pedidos dentro de la misma ventana de tiempo:

1. Se mide la referencia: 100,000 pedidos con grado de paralelismo 1.
2. Para los grados 2, 4, 8 y 16 se prueba la hipótesis ideal (100,000 × p) y se ajusta la cantidad de pedidos hasta que el tiempo quede dentro de la referencia ±10 %.
3. Se calcula el factor de capacidad `C = N_p / N_1`, el throughput (pedidos por segundo) y se compara con el modelo de Gustafson `S = p - f (p - 1)` con `f = 5 %`.

El análisis está al final del archivo, en el comentario `/* Mi análisis ... */`.

```bash
dotnet run -c Release --project LeyGustafsonPedidos
```
