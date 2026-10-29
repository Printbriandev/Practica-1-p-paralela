/*
 * ==================================================================
 *   Instituto Tecnológico de Las Américas (ITLA)
 *   Programación Paralela
 *
 *   Práctica de Laboratorio 4
 *   Transposición de Matrices Paralela y Localidad de Caché en C#
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
using System.Threading.Tasks;

class Program
{
    // Tamaños de matriz a probar (2000 no es potencia de 2, sirve para comparar con 2048)
    static readonly int[] Tamanos = { 1024, 2000, 2048 };

    // Lado del bloque para el recorrido por bloques: 32 x 32 doubles = 8 KB por bloque
    const int Bloque = 32;

    // Una ejecución de calentamiento y luego Repeticiones mediciones (se reporta la mediana)
    const int Repeticiones = 11;

    // Diferencia máxima aceptada entre la versión paralela y la secuencial
    const double Tolerancia = 1e-7;

    // Mayor diferencia encontrada en las validaciones del tamaño actual
    static double diferenciaMaxima;

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        Console.WriteLine("====================================================================");
        Console.WriteLine(" Lab 4: Transposición de matrices y localidad de caché");
        Console.WriteLine("====================================================================");
        Console.WriteLine($" Procesadores lógicos: {Environment.ProcessorCount}   Bloque: {Bloque}x{Bloque}   Repeticiones: {Repeticiones} (mediana)");
        Console.WriteLine($" Validación: |paralela[i,j] - secuencial[i,j]| < {Tolerancia:0E+0} en todas las celdas");

        string[] nombres =
        {
            "1. Secuencial ingenua", "2a. Secuencial bucles j-i", "2b. Secuencial por bloques",
            "3a. Parallel.For por filas", "3b. Parallel.For por bloques"
        };
        var tiempos = new double[nombres.Length, Tamanos.Length];
        var validaciones = new string[Tamanos.Length];

        for (int t = 0; t < Tamanos.Length; t++)
        {
            int n = Tamanos[t];
            double[,] matriz = GenerarMatriz(n);
            double[,] referencia = new double[n, n];
            double[,] resultado = new double[n, n];
            diferenciaMaxima = 0;

            // La versión ingenua es la referencia de tiempo y de resultado
            tiempos[0, t] = Medir(() => TransponerIngenua(matriz, referencia, n));
            string validacion = EsTranspuesta(matriz, referencia, n) ? "transpuesta OK" : "transpuesta ERROR";

            Action[] versiones =
            {
                () => TransponerBuclesIntercambiados(matriz, resultado, n),
                () => TransponerPorBloques(matriz, resultado, n),
                () => TransponerParalelaIngenua(matriz, resultado, n),
                () => TransponerParalelaPorBloques(matriz, resultado, n)
            };
            for (int v = 0; v < versiones.Length; v++)
            {
                tiempos[v + 1, t] = Medir(versiones[v]);
                validacion += $" | {nombres[v + 1].Split('.')[0]} {Validar(referencia, resultado, n)}";
            }
            validaciones[t] = validacion + $" | diferencia máxima = {diferenciaMaxima:0.0E+0}";
        }

        // Tabla de tiempos (ms) con el speedup frente a la versión ingenua del mismo tamaño
        Console.WriteLine();
        Console.WriteLine(" ------------------------------------------------------------------------------------");
        Console.Write("   Versión                       ");
        foreach (int n in Tamanos)
            Console.Write($"{n + " x " + n,18}");
        Console.WriteLine();
        Console.WriteLine(" ------------------------------------------------------------------------------------");
        for (int v = 0; v < nombres.Length; v++)
        {
            Console.Write($"   {nombres[v],-30}");
            for (int t = 0; t < Tamanos.Length; t++)
                Console.Write($"{tiempos[v, t],8:F2} ms {tiempos[0, t] / tiempos[v, t],5:F2}x");
            Console.WriteLine();
        }
        Console.WriteLine(" ------------------------------------------------------------------------------------");
        Console.Write("   Speedup 3b vs. mejor secuencial");
        for (int t = 0; t < Tamanos.Length; t++)
        {
            double mejorSecuencial = Math.Min(tiempos[0, t], Math.Min(tiempos[1, t], tiempos[2, t]));
            Console.Write($"{mejorSecuencial / tiempos[4, t],17:F2}x");
        }
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine(" Validación de cada versión contra la secuencial ingenua (1):");
        for (int t = 0; t < Tamanos.Length; t++)
            Console.WriteLine($"   {Tamanos[t]}: {validaciones[t]}");
    }

    // Matriz n x n con valores aleatorios en [0, 1000) y semilla fija
    static double[,] GenerarMatriz(int n)
    {
        var aleatorio = new Random(2026);
        var matriz = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                matriz[i, j] = aleatorio.NextDouble() * 1000.0;
        return matriz;
    }

    // 1. Ingenua: lee por filas (contiguo) pero escribe por columnas (saltos de n * 8 bytes)
    static void TransponerIngenua(double[,] origen, double[,] destino, int n)
    {
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                destino[j, i] = origen[i, j];
    }

    // 2a. Bucles intercambiados: ahora escribe por filas (contiguo) pero lee por columnas
    static void TransponerBuclesIntercambiados(double[,] origen, double[,] destino, int n)
    {
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                destino[j, i] = origen[i, j];
    }

    // 2b. Por bloques (loop tiling): se transpone un bloque de 32 x 32 a la vez, de modo que las
    // líneas de caché de origen y destino que usa el bloque caben juntas en la caché L1
    static void TransponerPorBloques(double[,] origen, double[,] destino, int n)
    {
        for (int ii = 0; ii < n; ii += Bloque)
            TransponerFilaDeBloques(origen, destino, n, ii);
    }

    // 3a. Paralela ingenua: cada iteración de Parallel.For transpone una fila completa
    static void TransponerParalelaIngenua(double[,] origen, double[,] destino, int n)
    {
        Parallel.For(0, n, i =>
        {
            for (int j = 0; j < n; j++)
                destino[j, i] = origen[i, j];
        });
    }

    // 3b. Paralela por bloques: cada iteración de Parallel.For transpone una franja de 32 filas
    static void TransponerParalelaPorBloques(double[,] origen, double[,] destino, int n)
    {
        int franjas = (n + Bloque - 1) / Bloque;
        Parallel.For(0, franjas, f => TransponerFilaDeBloques(origen, destino, n, f * Bloque));
    }

    // Transpone todos los bloques de la franja de filas [ii, ii + Bloque)
    static void TransponerFilaDeBloques(double[,] origen, double[,] destino, int n, int ii)
    {
        int finI = Math.Min(ii + Bloque, n);
        for (int jj = 0; jj < n; jj += Bloque)
        {
            int finJ = Math.Min(jj + Bloque, n);
            for (int i = ii; i < finI; i++)
                for (int j = jj; j < finJ; j++)
                    destino[j, i] = origen[i, j];
        }
    }

    // Comprueba la definición de transpuesta: destino[j, i] == origen[i, j]
    static bool EsTranspuesta(double[,] origen, double[,] destino, int n)
    {
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                if (destino[j, i] != origen[i, j])
                    return false;
        return true;
    }

    // Compara con la referencia usando |resultado - referencia| < Tolerancia y limpia el resultado
    static string Validar(double[,] referencia, double[,] resultado, int n)
    {
        long errores = 0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                double diferencia = Math.Abs(resultado[i, j] - referencia[i, j]);
                if (diferencia > diferenciaMaxima)
                    diferenciaMaxima = diferencia;
                if (!(diferencia < Tolerancia))
                    errores++;
            }
        Array.Clear(resultado);
        return errores == 0 ? "OK" : $"ERROR ({errores} celdas)";
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
