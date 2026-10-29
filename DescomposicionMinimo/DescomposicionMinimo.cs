/*
 * ==================================================================
 *   Instituto Tecnológico de Las Américas (ITLA)
 *   Programación Paralela
 *
 *   Práctica de Laboratorio 3
 *   Descomposición de Tareas, Grafos de Dependencia y Mapeo
 *
 *   Estudiante : Brian Amaury Guzman Mañon
 *   Matrícula  : 2024-1286
 *   Docente    : Erick Leonardo Perez Veloz
 *   Fecha      : 28 de octubre de 2026
 *   Repositorio: https://github.com/Printbriandev/Practica-1-p-paralela
 *
 *   Equipo de pruebas:
 *     CPU   : AMD Ryzen 5 7535HS (6 núcleos físicos, 12 lógicos)
 *     SO    : Windows 11 Home, conectado a la corriente
 *     .NET  : SDK 10.0.401, compilado en modo Release
 *
 *   Ejecución:  dotnet run -c Release
 * ==================================================================
 */

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    // Tamaño del arreglo y semilla fija para que todas las corridas usen los mismos datos
    const int N = 50_000_000;
    const int Semilla = 2026;

    // Cada enfoque se ejecuta una vez de calentamiento y luego Repeticiones veces (se reporta la mediana)
    const int Repeticiones = 7;

    // Umbrales de granularidad del Enfoque B (1,000 es un caso extremo extra para ver la sobre-descomposición)
    static readonly int[] Umbrales = { 1_000, 10_000, 1_000_000, 10_000_000 };

    // Contador de tareas creadas con Task.Run en el Enfoque B
    static int tareasCreadas;

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int p = Environment.ProcessorCount;

        Console.WriteLine("==============================================================");
        Console.WriteLine(" Lab 3: Mínimo de 50,000,000 enteros (datos vs. recursiva)");
        Console.WriteLine("==============================================================");
        Console.WriteLine($" Procesadores lógicos (Environment.ProcessorCount): {p}");
        Console.WriteLine($" Repeticiones por medición: {Repeticiones} (se reporta la mediana)");
        Console.WriteLine();

        int[] datos = GenerarDatos();

        // Referencia secuencial
        int minSecuencial = 0;
        double tSecuencial = Medir(() => minSecuencial = MinimoSecuencial(datos, 0, datos.Length));
        Console.WriteLine($" Secuencial                 : {tSecuencial,8:F2} ms   mínimo = {minSecuencial:N0}");

        // Enfoque A: descomposición de datos estática
        int minA = 0;
        double tA = Medir(() => minA = MinimoEnfoqueA(datos, p));
        Console.WriteLine($" Enfoque A ({p} particiones) : {tA,8:F2} ms   mínimo = {minA:N0}   speedup = {tSecuencial / tA:F2}x");
        Console.WriteLine();

        // Enfoque B: descomposición recursiva con distintos umbrales
        Console.WriteLine(" Enfoque B (recursivo con Task.Run)");
        Console.WriteLine(" --------------------------------------------------------------------------");
        Console.WriteLine("   Umbral        Tiempo (ms)   Speedup   Tareas   Niveles   Mínimo");
        Console.WriteLine(" --------------------------------------------------------------------------");

        bool todoCorrecto = minA == minSecuencial;
        foreach (int umbral in Umbrales)
        {
            int minB = 0;
            double tB = Medir(() =>
            {
                tareasCreadas = 0;
                minB = MinimoEnfoqueB(datos, 0, datos.Length, umbral);
            });
            todoCorrecto &= minB == minSecuencial;

            Console.WriteLine($"   {umbral,10:N0}   {tB,10:F2}   {tSecuencial / tB,6:F2}x   {tareasCreadas,6:N0}   {Niveles(umbral),7}   {minB:N0}");
        }
        Console.WriteLine(" --------------------------------------------------------------------------");
        Console.WriteLine();
        Console.WriteLine(todoCorrecto
            ? " Validación: todos los enfoques encontraron el mismo mínimo que la versión secuencial."
            : " Validación: ERROR, algún enfoque no coincide con la versión secuencial.");
        Console.WriteLine($" Hilos del ThreadPool al terminar: {ThreadPool.ThreadCount}");
    }

    // Llena el arreglo con enteros aleatorios en [0, int.MaxValue)
    static int[] GenerarDatos()
    {
        var aleatorio = new Random(Semilla);
        var datos = new int[N];
        for (int i = 0; i < N; i++)
            datos[i] = aleatorio.Next();
        return datos;
    }

    // Recorre el segmento [inicio, fin) y devuelve su mínimo
    static int MinimoSecuencial(int[] datos, int inicio, int fin)
    {
        int minimo = int.MaxValue;
        for (int i = inicio; i < fin; i++)
            if (datos[i] < minimo)
                minimo = datos[i];
        return minimo;
    }

    // Enfoque A: p particiones fijas (una por procesador lógico), una Task por partición
    // y reducción secuencial de los p mínimos locales
    static int MinimoEnfoqueA(int[] datos, int p)
    {
        var minimosLocales = new int[p];
        var tareas = new Task[p];
        int tamanoBloque = datos.Length / p;

        for (int k = 0; k < p; k++)
        {
            int id = k;
            int inicio = id * tamanoBloque;
            // La última partición se queda con el residuo de la división
            int fin = id == p - 1 ? datos.Length : inicio + tamanoBloque;
            tareas[id] = Task.Run(() => minimosLocales[id] = MinimoSecuencial(datos, inicio, fin));
        }
        Task.WaitAll(tareas);

        int minimoGlobal = int.MaxValue;
        for (int k = 0; k < p; k++)
            if (minimosLocales[k] < minimoGlobal)
                minimoGlobal = minimosLocales[k];
        return minimoGlobal;
    }

    // Enfoque B: divide el segmento en dos mitades; la izquierda va a una Task nueva y la derecha
    // la resuelve el hilo actual. Si el segmento es menor o igual que el umbral, se resuelve secuencial.
    static int MinimoEnfoqueB(int[] datos, int inicio, int fin, int umbral)
    {
        if (fin - inicio <= umbral)
            return MinimoSecuencial(datos, inicio, fin);

        int medio = inicio + (fin - inicio) / 2;

        Interlocked.Increment(ref tareasCreadas);
        Task<int> izquierda = Task.Run(() => MinimoEnfoqueB(datos, inicio, medio, umbral));
        int derecha = MinimoEnfoqueB(datos, medio, fin, umbral);

        // Combinación de abajo hacia arriba
        return Math.Min(izquierda.Result, derecha);
    }

    // Cantidad de niveles de división hasta que el segmento cabe en el umbral
    static int Niveles(int umbral)
    {
        int niveles = 0;
        long tamano = N;
        while (tamano > umbral)
        {
            tamano = (tamano + 1) / 2;
            niveles++;
        }
        return niveles;
    }

    // Ejecuta una vez de calentamiento y devuelve la mediana de Repeticiones mediciones en ms
    static double Medir(Action accion)
    {
        accion();
        var tiempos = new double[Repeticiones];
        for (int r = 0; r < Repeticiones; r++)
        {
            var reloj = Stopwatch.StartNew();
            accion();
            reloj.Stop();
            tiempos[r] = reloj.Elapsed.TotalMilliseconds;
        }
        Array.Sort(tiempos);
        return tiempos[Repeticiones / 2];
    }
}
