/*
 * ==================================================================
 *   Instituto Tecnológico de Las Américas (ITLA)
 *   Programación Paralela
 *
 *   Asignación Teórica 6 (experimento de apoyo)
 *   Algoritmos de Prefijo (Scan) y Complejidad en Paralelo
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
using System.Threading.Tasks;

class Program
{
    // Tamaños: 2^20 (8 MB, cabe en la L3 de 16 MB) y 2^24 (128 MB, muy superior a la caché)
    static readonly int[] LogNs = { 20, 24 };
    const int Repeticiones = 11;

    // Cantidades de hilos para la versión por bloques
    static readonly int[] Hilos = { 1, 2, 4, 6, 8, 12 };

    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(" Asignación 6: suma de prefijos (scan inclusivo) de enteros long");
        Console.WriteLine($" Procesadores lógicos: {Environment.ProcessorCount}   Mediana de {Repeticiones} ejecuciones tras un calentamiento");
        Console.WriteLine();
        Console.WriteLine("   n                 Algoritmo                       p    Tiempo (ms)   Speedup   Trabajo (sumas)   Pasos sinc.   Igual a sec.");
        Console.WriteLine(" -------------------------------------------------------------------------------------------------------------------");

        foreach (int logN in LogNs)
        {
            int n = 1 << logN;
            long[] datos = new long[n];
            var aleatorio = new Random(2026);
            for (int i = 0; i < n; i++)
                datos[i] = aleatorio.Next(0, 1000);

            long[] referencia = new long[n];
            long[] resultado = new long[n];
            long[] auxiliar = new long[n];
            string etiqueta = $"2^{logN} = {n:N0}";

            double tSec = Medir(() => ScanSecuencial(datos, referencia));
            Imprimir(etiqueta, "Secuencial", 1, tSec, tSec, n - 1L, 0, true);

            double tHs = Medir(() => ScanHillisSteele(datos, resultado, auxiliar));
            Imprimir("", "Hillis-Steele (Parallel.For)", Environment.ProcessorCount, tHs, tSec, (long)n * logN - (n - 1L), logN, Iguales(referencia, resultado));

            double tBl = Medir(() => ScanBlelloch(datos, resultado));
            Imprimir("", "Blelloch (Parallel.For)", Environment.ProcessorCount, tBl, tSec, 2L * (n - 1) + n, 2 * logN + 1, Iguales(referencia, resultado));

            foreach (int p in Hilos)
            {
                double tBq = Medir(() => ScanPorBloques(datos, resultado, p));
                Imprimir("", "Por bloques (3 fases)", p, tBq, tSec, 2L * n - p, 2, Iguales(referencia, resultado));
            }
            Console.WriteLine($"   {"",-17} Costo por suma secuencial t_c = T_sec / n = {tSec * 1e6 / n:F3} ns");
            Console.WriteLine(" -------------------------------------------------------------------------------------------------------------------");
        }
    }

    // Scan inclusivo secuencial: salida[i] = datos[0] + ... + datos[i]
    static void ScanSecuencial(long[] datos, long[] salida)
    {
        long acumulado = 0;
        for (int i = 0; i < datos.Length; i++)
        {
            acumulado += datos[i];
            salida[i] = acumulado;
        }
    }

    // Hillis-Steele: log2(n) pasos; en el paso d cada i >= 2^d suma el elemento 2^d posiciones atrás
    static void ScanHillisSteele(long[] datos, long[] salida, long[] auxiliar)
    {
        Array.Copy(datos, salida, datos.Length);
        long[] origen = salida, destino = auxiliar;
        for (int salto = 1; salto < datos.Length; salto <<= 1)
        {
            long[] o = origen, d = destino;
            int s = salto;
            Parallel.For(0, datos.Length, i => d[i] = i >= s ? o[i] + o[i - s] : o[i]);
            (origen, destino) = (destino, origen);
        }
        if (!ReferenceEquals(origen, salida))
            Array.Copy(origen, salida, datos.Length);
    }

    // Blelloch: barrido ascendente (reducción en árbol) y descendente (reparto); da un scan exclusivo
    // que se convierte en inclusivo sumando el dato original
    static void ScanBlelloch(long[] datos, long[] salida)
    {
        int n = datos.Length;
        Array.Copy(datos, salida, n);
        for (int salto = 1; salto < n; salto <<= 1)                 // up-sweep
        {
            int s = salto, nodos = n / (2 * salto);
            Parallel.For(0, nodos, k => { int i = (k + 1) * 2 * s - 1; salida[i] += salida[i - s]; });
        }
        salida[n - 1] = 0;
        for (int salto = n / 2; salto >= 1; salto >>= 1)            // down-sweep
        {
            int s = salto, nodos = n / (2 * salto);
            Parallel.For(0, nodos, k =>
            {
                int i = (k + 1) * 2 * s - 1;
                long izquierdo = salida[i - s];
                salida[i - s] = salida[i];
                salida[i] += izquierdo;
            });
        }
        Parallel.For(0, n, i => salida[i] += datos[i]);           // exclusivo -> inclusivo
    }

    // Por bloques con p hilos: (1) suma de cada bloque, (2) prefijo secuencial de las p sumas,
    // (3) scan local de cada bloque empezando en su desplazamiento
    static void ScanPorBloques(long[] datos, long[] salida, int p)
    {
        int n = datos.Length, tamano = (n + p - 1) / p;
        var sumas = new long[p];
        var opciones = new ParallelOptions { MaxDegreeOfParallelism = p };

        // Fase 1 (paralela): suma total de cada bloque
        Parallel.For(0, p, opciones, t => sumas[t] = SumaBloque(datos, t * tamano, Math.Min((t + 1) * tamano, n)));

        // Fase 2 (secuencial, p pasos): prefijo exclusivo de las sumas = desplazamiento de cada bloque
        long desplazamiento = 0;
        for (int t = 0; t < p; t++)
        {
            long s = sumas[t];
            sumas[t] = desplazamiento;
            desplazamiento += s;
        }

        // Fase 3 (paralela): scan local de cada bloque a partir de su desplazamiento
        Parallel.For(0, p, opciones, t => ScanBloque(datos, salida, t * tamano, Math.Min((t + 1) * tamano, n), sumas[t]));
    }

    static long SumaBloque(long[] datos, int inicio, int fin)
    {
        long s = 0;
        for (int i = inicio; i < fin; i++)
            s += datos[i];
        return s;
    }

    static void ScanBloque(long[] datos, long[] salida, int inicio, int fin, long acumulado)
    {
        for (int i = inicio; i < fin; i++)
        {
            acumulado += datos[i];
            salida[i] = acumulado;
        }
    }

    static bool Iguales(long[] a, long[] b)
    {
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    static void Imprimir(string tamano, string nombre, int p, double t, double tSec, long trabajo, int pasos, bool ok)
    {
        Console.WriteLine($"   {tamano,-17} {nombre,-30} {p,4}   {t,10:F2}   {tSec / t,6:F2}x   {trabajo,15:N0}   {pasos,11}   {(ok ? "OK" : "ERROR")}");
    }

    // Ejecuta una vez de calentamiento y devuelve la mediana de las mediciones en ms
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
