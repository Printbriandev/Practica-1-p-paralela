/*
 * ==================================================================
 *   Instituto Tecnológico de Las Américas (ITLA)
 *   Programación Paralela
 *
 *   Práctica de Laboratorio 5
 *   Reducción Paralela, Contención de Hilos y Estado Local en C#
 *
 *   Estudiante : Brian Amaury Guzman Mañon
 *   Matrícula  : 2024-1286
 *   Docente    : Erick Leonardo Perez Veloz
 *   Fecha      : 11 de noviembre de 2026
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
    // Tamaños del arreglo (dentro del rango pedido: 1,000,000 a 10,000,000)
    static readonly int[] Tamanos = { 1_000_000, 5_000_000, 10_000_000 };

    // Tolerancia de la validación: |suma_paralela - suma_secuencial| < Epsilon
    const double Epsilon = 1e-8;

    // Repeticiones por medición (la versión con lock masivo es muy lenta y se repite menos)
    const int Repeticiones = 7;
    const int RepeticionesLock = 3;

    // Candado compartido por las versiones paralelas
    static readonly object candado = new object();

    // Cantidad de veces que se ejecutó localFinally (una por tarea del Parallel.For)
    static int llamadasLocalFinally;

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(" Lab 5: Reducción paralela (suma de doubles), contención de hilos y estado local");
        Console.WriteLine($" Procesadores lógicos: {Environment.ProcessorCount}   Datos: double uniformes en [-1, 1), semilla 2026   ε = {Epsilon:0E+0}");
        Console.WriteLine($" Mediana de {Repeticiones} ejecuciones ({RepeticionesLock} para la versión con lock masivo), después de un calentamiento");
        Console.WriteLine("   N            Versión                 Tiempo (ms)  Speedup   |dif| vs. secuencial  ε     Locks        Contenciones");
        Console.WriteLine(" -------------------------------------------------------------------------------------------------------------------");

        bool todoValido = true;
        foreach (int n in Tamanos)
        {
            double[] datos = GenerarDatos(n, -1.0, 1.0);

            double sumaSec = 0, sumaLock = 0, sumaLocal = 0, sumaBloques = 0;
            double tSec = Medir(() => sumaSec = SumaSecuencial(datos), Repeticiones);
            double tLock = Medir(() => sumaLock = SumaLockMasivo(datos), RepeticionesLock);
            double tLocal = Medir(() => sumaLocal = SumaEstadoLocal(datos), Repeticiones);
            double tBloques = Medir(() => sumaBloques = SumaEstadoLocalPorBloques(datos), Repeticiones);

            // Contención: se cuenta en una ejecución aparte de cada versión paralela
            long contLock = Contenciones(() => SumaLockMasivo(datos));
            llamadasLocalFinally = 0;
            long contLocal = Contenciones(() => SumaEstadoLocal(datos));
            int locksLocal = llamadasLocalFinally;
            llamadasLocalFinally = 0;
            long contBloques = Contenciones(() => SumaEstadoLocalPorBloques(datos));
            int locksBloques = llamadasLocalFinally;

            double difLock = Math.Abs(sumaLock - sumaSec);
            double difLocal = Math.Abs(sumaLocal - sumaSec);
            double difBloques = Math.Abs(sumaBloques - sumaSec);
            todoValido &= difLock < Epsilon && difLocal < Epsilon && difBloques < Epsilon;

            Console.WriteLine($"   {n,10:N0}   1. Secuencial            {tSec,10:F2}   {1.0,6:F2}x   {"(referencia)",-20}  -     {0,10:N0}   {0,12:N0}");
            Console.WriteLine($"   {"",10}   2. Parallel.For + lock   {tLock,10:F2}   {tSec / tLock,6:F2}x   {difLock,-20:0.000E+00}  {(difLock < Epsilon ? "OK" : "NO"),-4}  {n,10:N0}   {contLock,12:N0}");
            Console.WriteLine($"   {"",10}   3. Estado local          {tLocal,10:F2}   {tSec / tLocal,6:F2}x   {difLocal,-20:0.000E+00}  {(difLocal < Epsilon ? "OK" : "NO"),-4}  {locksLocal,10:N0}   {contLocal,12:N0}");
            Console.WriteLine($"   {"",10}   3b. Estado local bloques {tBloques,10:F2}   {tSec / tBloques,6:F2}x   {difBloques,-20:0.000E+00}  {(difBloques < Epsilon ? "OK" : "NO"),-4}  {locksBloques,10:N0}   {contBloques,12:N0}");
            Console.WriteLine($"   {"",10}   Sumas: secuencial = {sumaSec:F12}   estado local = {sumaLocal:F12}   bloques = {sumaBloques:F12}");
        }
        Console.WriteLine(" -------------------------------------------------------------------------------------------------------------------");
        Console.WriteLine(todoValido
            ? " Validación: en todos los tamaños |suma_paralela - suma_secuencial| < 1E-8. OK"
            : " Validación: ALGUNA versión paralela supera la tolerancia de 1E-8.");

        ExperimentoMagnitud();
    }

    // Arreglo de n doubles uniformes en [minimo, maximo) con semilla fija
    static double[] GenerarDatos(int n, double minimo, double maximo)
    {
        var aleatorio = new Random(2026);
        var datos = new double[n];
        for (int i = 0; i < n; i++)
            datos[i] = minimo + aleatorio.NextDouble() * (maximo - minimo);
        return datos;
    }

    // 1. Secuencial simple: un solo acumulador, sumado en orden 0, 1, 2, ...
    static double SumaSecuencial(double[] datos)
    {
        double suma = 0.0;
        for (int i = 0; i < datos.Length; i++)
            suma += datos[i];
        return suma;
    }

    // 2. Paralela directa: todos los hilos suman sobre la misma variable y cada iteración toma el lock
    static double SumaLockMasivo(double[] datos)
    {
        double suma = 0.0;
        Parallel.For(0, datos.Length, i =>
        {
            lock (candado)
            {
                suma += datos[i];
            }
        });
        return suma;
    }

    // 3. Paralela con estado local: cada tarea acumula en su propio subtotal (sin lock) y solo
    //    al terminar suma ese subtotal al total compartido, tomando el lock una vez por tarea
    static double SumaEstadoLocal(double[] datos)
    {
        double suma = 0.0;
        Parallel.For(0, datos.Length,
            () => 0.0,                                          // localInit: subtotal inicial de la tarea
            (i, estado, subtotal) => subtotal + datos[i],       // body: suma sin sincronización
            subtotal =>                                         // localFinally: combina una vez por tarea
            {
                Interlocked.Increment(ref llamadasLocalFinally);
                lock (candado)
                {
                    suma += subtotal;
                }
            });
        return suma;
    }

    // 3b. Estado local por bloques: igual que la versión 3, pero cada iteración del Parallel.For
    //     recorre un bloque contiguo con un for normal, así el delegado se invoca una vez por bloque
    //     y no una vez por elemento
    static double SumaEstadoLocalPorBloques(double[] datos)
    {
        int bloques = Environment.ProcessorCount * 8;
        int tamano = (datos.Length + bloques - 1) / bloques;
        double suma = 0.0;
        Parallel.For(0, bloques,
            () => 0.0,
            (b, estado, subtotal) =>
            {
                int inicio = b * tamano;
                int fin = Math.Min(inicio + tamano, datos.Length);
                for (int i = inicio; i < fin; i++)
                    subtotal += datos[i];
                return subtotal;
            },
            subtotal =>
            {
                Interlocked.Increment(ref llamadasLocalFinally);
                lock (candado)
                {
                    suma += subtotal;
                }
            });
        return suma;
    }

    // Suma compensada de Neumaier: referencia de alta precisión para medir el error de redondeo
    static double SumaCompensada(double[] datos)
    {
        double suma = 0.0, compensacion = 0.0;
        foreach (double x in datos)
        {
            double t = suma + x;
            if (Math.Abs(suma) >= Math.Abs(x))
                compensacion += (suma - t) + x;
            else
                compensacion += (x - t) + suma;
            suma = t;
        }
        return suma + compensacion;
    }

    // Experimento extra: la misma comparación con valores positivos grandes, donde la tolerancia
    // absoluta de 1E-8 deja de ser adecuada y hace falta una tolerancia relativa
    static void ExperimentoMagnitud()
    {
        int n = 10_000_000;
        double[] datos = GenerarDatos(n, 0.0, 1000.0);
        double sumaSec = SumaSecuencial(datos);
        double sumaLocal = SumaEstadoLocalPorBloques(datos);
        double referencia = SumaCompensada(datos);
        double dif = Math.Abs(sumaLocal - sumaSec);

        Console.WriteLine();
        Console.WriteLine(" Experimento extra: 10,000,000 doubles en [0, 1000) (suma del orden de 5E+9)");
        Console.WriteLine($"   Secuencial = {sumaSec:F6}   Estado local (3b) = {sumaLocal:F6}   Compensada (Neumaier) = {referencia:F6}");
        Console.WriteLine($"   |dif| absoluta = {dif:0.000E+00}  (< 1E-8: {(dif < Epsilon ? "sí" : "no")})   |dif| relativa = {dif / Math.Abs(sumaSec):0.000E+00}  (< 1E-8: {(dif / Math.Abs(sumaSec) < Epsilon ? "sí" : "no")})");
        Console.WriteLine($"   Error frente a la compensada: secuencial = {Math.Abs(sumaSec - referencia):0.000E+00}   estado local (3b) = {Math.Abs(sumaLocal - referencia):0.000E+00}");
    }

    // Diferencia de Monitor.LockContentionCount antes y después de ejecutar la acción una vez
    static long Contenciones(Action accion)
    {
        long antes = Monitor.LockContentionCount;
        accion();
        return Monitor.LockContentionCount - antes;
    }

    // Ejecuta una vez de calentamiento y devuelve la mediana de las mediciones en ms
    static double Medir(Action accion, int repeticiones)
    {
        accion();
        var tiempos = new double[repeticiones];
        for (int r = 0; r < repeticiones; r++)
        {
            var reloj = Stopwatch.StartNew();
            accion();
            reloj.Stop();
            tiempos[r] = reloj.Elapsed.TotalMilliseconds;
        }
        Array.Sort(tiempos);
        return tiempos[repeticiones / 2];
    }
}
