/*
 * ==================================================================
 *   Instituto Tecnológico de Las Américas (ITLA)
 *   Programación Paralela
 *
 *   Práctica de Laboratorio 1
 *   Ley de Amdahl y Análisis de Rendimiento Concurrente
 *
 *   Estudiante : Brian Amaury Guzman Mañon
 *   Matrícula  : 2024-1286
 *   Docente    : Erick Leonardo Perez Veloz
 *   Fecha      : 14 de octubre de 2026
 *
 *   Equipo de pruebas:
 *     CPU   : AMD Ryzen 5 7535HS (6 núcleos físicos, 12 lógicos)
 *     SO    : Windows 11 Home
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
    const int TotalPedidos = 30000;
    const int Iteraciones = 1500;

    // Cuantas veces se repite cada medicion. Se reporta la mediana
    // para que un "pico" aislado del sistema operativo no distorsione la tabla.
    const int Repeticiones = 3;

    static readonly int[] GradosParalelismo = { 1, 2, 4, 8, 16 };

    // Objeto exclusivo para el lock (nunca se hace lock sobre "this" o un string).
    static readonly object candado = new object();

    // ------------------------------------------------------------
    // Trabajo de cada pedido (codigo original, sin cambios)
    // ------------------------------------------------------------
    static double ProcesarPedido(int pedidoId)
    {
        double resultado = 0;

        for (int j = 1; j <= Iteraciones; j++)
        {
            resultado += Math.Sqrt(pedidoId + j)
                       * Math.Sin(j * 0.01);
        }

        return resultado;
    }

    // ------------------------------------------------------------
    // Version secuencial (codigo original, sin cambios)
    // ------------------------------------------------------------
    static double ProcesarSecuencial()
    {
        double total = 0;

        for (int i = 0; i < TotalPedidos; i++)
        {
            total += ProcesarPedido(i);
        }

        return total;
    }

    // ------------------------------------------------------------
    // Paso 3a: version INCORRECTA, solo para demostrar la condicion de carrera.
    // "total += x" NO es atomico: son 3 pasos (leer total, sumar, escribir total).
    // Si dos hilos leen el mismo valor viejo, una de las dos sumas se pierde.
    // ------------------------------------------------------------
    static double ProcesarParaleloSinLock(int hilos)
    {
        double total = 0;

        ParallelOptions opciones = new ParallelOptions
        {
            MaxDegreeOfParallelism = hilos
        };

        Parallel.For(0, TotalPedidos, opciones, i =>
        {
            total += ProcesarPedido(i); // ERROR intencional: carrera de datos
        });

        return total;
    }

    // ------------------------------------------------------------
    // Pasos 2 y 3b: version paralela CORRECTA con lock en cada pedido.
    // El calculo pesado (ProcesarPedido) se hace FUERA del lock;
    // solo la suma a la variable compartida queda protegida.
    // ------------------------------------------------------------
    static double ProcesarParalelo(int hilos)
    {
        double total = 0;

        ParallelOptions opciones = new ParallelOptions
        {
            MaxDegreeOfParallelism = hilos
        };

        Parallel.For(0, TotalPedidos, opciones, i =>
        {
            double valor = ProcesarPedido(i);

            lock (candado)
            {
                total += valor;
            }
        });

        return total;
    }

    // ------------------------------------------------------------
    // Paso 5: acumuladores locales.
    // Cada hilo suma en su propia variable "subtotal" (sin lock),
    // y solo al terminar su parte entra UNA vez al lock para aportar al total.
    // Resultado: ~30,000 locks se convierten en ~(numero de hilos) locks.
    // ------------------------------------------------------------
    static double ProcesarParaleloLocal(int hilos)
    {
        double total = 0;

        ParallelOptions opciones = new ParallelOptions
        {
            MaxDegreeOfParallelism = hilos
        };

        Parallel.For(
            0, TotalPedidos, opciones,
            // localInit: valor inicial del acumulador de cada hilo
            () => 0.0,
            // body: trabaja solo con el acumulador local, sin lock
            (i, estado, subtotal) => subtotal + ProcesarPedido(i),
            // localFinally: se ejecuta una vez por hilo al final
            subtotal =>
            {
                lock (candado)
                {
                    total += subtotal;
                }
            });

        return total;
    }

    // ------------------------------------------------------------
    // Utilidades de medicion
    // ------------------------------------------------------------

    // Ejecuta la funcion varias veces y devuelve la mediana del tiempo (ms).
    static double Medir(Func<double> funcion, out double resultado)
    {
        double[] tiempos = new double[Repeticiones];
        resultado = 0;

        for (int r = 0; r < Repeticiones; r++)
        {
            Stopwatch reloj = Stopwatch.StartNew();
            resultado = funcion();
            reloj.Stop();
            tiempos[r] = reloj.Elapsed.TotalMilliseconds;
        }

        Array.Sort(tiempos);
        return tiempos[Repeticiones / 2];
    }

    // Las sumas en paralelo se hacen en otro ORDEN que la secuencial, y en
    // punto flotante (a + b) + c no siempre es exactamente a + (b + c).
    // Por eso se compara con una tolerancia relativa muy pequena, no con ==.
    static bool Coincide(double esperado, double obtenido)
    {
        return Math.Abs(esperado - obtenido) <= Math.Abs(esperado) * 1e-9;
    }

    static double ImprimirTabla(string titulo, Func<int, double> metodo,
                                double tiempoSecuencial, double resultadoSecuencial)
    {
        Console.WriteLine();
        Console.WriteLine(titulo);
        Console.WriteLine("Grado | Tiempo (ms) | Speedup | Eficiencia | Resultado correcto");
        Console.WriteLine("------+-------------+---------+------------+-------------------");

        double mejorTiempo = double.MaxValue;
        int mejorGrado = 0;

        foreach (int p in GradosParalelismo)
        {
            double resultado;
            double tiempo = Medir(() => metodo(p), out resultado);

            // Speedup    S(p) = T_secuencial / T_paralelo(p)
            // Eficiencia E(p) = S(p) / p
            double speedup = tiempoSecuencial / tiempo;
            double eficiencia = speedup / p;

            Console.WriteLine(
                $"{p,5} | {tiempo,11:F1} | {speedup,6:F2}x | {eficiencia,9:P1} | {(Coincide(resultadoSecuencial, resultado) ? "Si" : "NO")}"
            );

            if (tiempo < mejorTiempo)
            {
                mejorTiempo = tiempo;
                mejorGrado = p;
            }
        }

        Console.WriteLine($"Menor tiempo: grado {mejorGrado} ({mejorTiempo:F1} ms)");
        return mejorTiempo;
    }

    static void Main()
    {
        Console.WriteLine("=== PROCESAMIENTO DE PEDIDOS ===");

        // ---------- Paso 1 ----------
        Console.WriteLine(
            $"Procesadores logicos: {Environment.ProcessorCount}"
        );
        Console.WriteLine($"Pedidos: {TotalPedidos:N0} | Iteraciones por pedido: {Iteraciones:N0}");
        Console.WriteLine($"Cada tiempo es la mediana de {Repeticiones} ejecuciones.");

        // Calentamiento: el compilador JIT traduce el codigo la primera vez
        // que se ejecuta; si no calentamos, ese costo se mezcla con la medicion.
        ProcesarSecuencial();
        ProcesarParalelo(Environment.ProcessorCount);
        ProcesarParaleloLocal(Environment.ProcessorCount);

        double resultadoSecuencial;
        double tiempoSecuencial = Medir(ProcesarSecuencial, out resultadoSecuencial);

        Console.WriteLine(
            $"Tiempo secuencial: {tiempoSecuencial:F1} ms"
        );
        Console.WriteLine($"Resultado: {resultadoSecuencial:F4}");

        // ---------- Paso 3a: demostrar la condicion de carrera ----------
        Console.WriteLine();
        Console.WriteLine("--- Suma concurrente SIN lock (incorrecta a proposito) ---");
        int incorrectos = 0;
        for (int intento = 1; intento <= 5; intento++)
        {
            double r = ProcesarParaleloSinLock(Environment.ProcessorCount);
            bool ok = Coincide(resultadoSecuencial, r);
            if (!ok) incorrectos++;
            Console.WriteLine(
                $"Intento {intento}: {r,22:F4}  diferencia: {resultadoSecuencial - r,18:F4}  {(ok ? "OK" : "INCORRECTO")}"
            );
        }
        Console.WriteLine($"Resultados incorrectos sin lock: {incorrectos} de 5");

        // ---------- Pasos 2, 3b y 4: lock en cada pedido ----------
        double mejorLock = ImprimirTabla(
            "--- Paralelo con lock en CADA pedido ---",
            ProcesarParalelo, tiempoSecuencial, resultadoSecuencial);

        // ---------- Paso 5: acumuladores locales ----------
        double mejorLocal = ImprimirTabla(
            "--- Paralelo con acumuladores LOCALES (lock una vez por hilo) ---",
            ProcesarParaleloLocal, tiempoSecuencial, resultadoSecuencial);

        Console.WriteLine();
        Console.WriteLine(
            $"Mejor con lock por pedido: {mejorLock:F1} ms | Mejor con acumuladores locales: {mejorLocal:F1} ms"
        );
    }
}

/*Mi análisis

==================================================================
RESULTADOS OBTENIDOS (AMD Ryzen 5 7535HS, 12 procesadores lógicos)
Cada tiempo es la mediana de 3 ejecuciones en modo Release.
==================================================================

Procesadores lógicos : 12  (6 núcleos físicos con SMT, 2 hilos por núcleo)
Tiempo secuencial    : 603.7 ms
Resultado secuencial : 628763357.8296

Speedup    S(p) = T_secuencial / T_paralelo(p)
Eficiencia E(p) = S(p) / p

Tabla 1: lock en CADA pedido (pasos 2, 3 y 4)

  Grado | Tiempo    | Speedup | Eficiencia | Resultado correcto
  ------+-----------+---------+------------+-------------------
    1   | 612.5 ms  |  0.99x  |   98.6 %   | Sí
    2   | 314.9 ms  |  1.92x  |   95.9 %   | Sí
    4   | 167.5 ms  |  3.60x  |   90.1 %   | Sí
    8   |  95.3 ms  |  6.33x  |   79.1 %   | Sí
   16   |  86.8 ms  |  6.95x  |   43.4 %   | Sí

  Menor tiempo: grado 16 (86.8 ms).

Tabla 2: acumuladores locales (paso 5)

  Grado | Tiempo    | Speedup | Eficiencia | Resultado correcto
  ------+-----------+---------+------------+-------------------
    1   | 555.3 ms  |  1.09x  |  108.7 %   | Sí
    2   | 318.7 ms  |  1.89x  |   94.7 %   | Sí
    4   | 160.2 ms  |  3.77x  |   94.2 %   | Sí
    8   |  94.3 ms  |  6.40x  |   80.0 %   | Sí
   16   |  82.6 ms  |  7.31x  |   45.7 %   | Sí

  Menor tiempo: grado 16 (82.6 ms).

Curva de aceleración (lock por pedido)

  Speedup
   8x |
   7x |                                   * (16) 6.95x
   6x |                       * (8) 6.33x
   5x |
   4x |           * (4) 3.60x
   3x |
   2x |     * (2) 1.92x
   1x | * (1) 0.99x
      +----+-----+-----+-----+-----+-----+-----+---- Grado
           1     2     4     8    12    16
  La curva sube casi en línea recta hasta 8 y luego se aplana:
  pasar de 8 a 16 solo aporta 0.62x adicional.


------------------------------------------------------------------
1) ¿Por qué no sería correcto comparar tiempos obtenidos con
   cantidades diferentes de pedidos?
------------------------------------------------------------------
Porque el Speedup solo tiene sentido si las dos versiones resuelven
EXACTAMENTE el mismo problema. Si la versión secuencial procesa
30,000 pedidos y la paralela 10,000, la paralela hace un tercio del
trabajo y parecería 3 veces más rápida aunque no se haya paralelizado
nada. La mejora vendría del tamaño de la entrada y no del paralelismo.
Además el resultado numérico cambia, así que tampoco podría verificar
que la versión paralela calcula lo mismo. Por eso TotalPedidos e
Iteraciones son constantes y se usan en todas las versiones.


------------------------------------------------------------------
3) Suma concurrente sin lock: riesgo y corrección
------------------------------------------------------------------
Al sumar en la variable compartida "total" sin protección, los 5
intentos dieron resultados INCORRECTOS, por ejemplo:

  Correcto   : 628,763,357.83
  Sin lock   :  64,368,352.50   (se perdió cerca del 90 % de la suma)

La causa es una condición de carrera. "total += ProcesarPedido(i)"
equivale a "total = total + ProcesarPedido(i)" y no es una operación
atómica:
   a) se LEE el valor actual de total,
   b) se calcula ProcesarPedido(i), que tarda unos 20 microsegundos,
   c) se ESCRIBE la suma en total.
Como total se lee ANTES del cálculo pesado, entre (a) y (c) hay mucho
tiempo. En ese intervalo los otros 11 hilos también leen el mismo
valor viejo, y cuando cada uno escribe borra lo que escribieron los
demás. Por eso no se pierden unos decimales, sino casi toda la suma.

Corrección: calcular primero el valor fuera del lock y luego proteger
solo la suma:

   double valor = ProcesarPedido(i);
   lock (candado) { total += valor; }

El lock garantiza que solo un hilo a la vez ejecuta la lectura,
suma y escritura de total. Con esto todas las ejecuciones coinciden
con el resultado secuencial.

Nota: el programa compara los resultados con una tolerancia relativa
de 1e-9 y no con ==. En paralelo los pedidos se suman en otro orden,
y en punto flotante (a + b) + c puede diferir de a + (b + c) en los
últimos dígitos decimales. Eso no es un error de sincronización.


------------------------------------------------------------------
4) ¿Qué grado produjo el menor tiempo?
------------------------------------------------------------------
El grado 16, con 86.8 ms (speedup 6.95x). Sin embargo, la ganancia
respecto al grado 8 (95.3 ms) es pequeña, y la eficiencia cae de
79.1 % a 43.4 %. El grado 8 es el que mejor aprovecha cada hilo; el
16 solo gana porque logra ocupar los 12 procesadores lógicos.


------------------------------------------------------------------
5) Acumuladores locales vs lock en cada pedido
------------------------------------------------------------------
Con lock por pedido se entra al lock 30,000 veces y los hilos hacen
fila para tocar la misma variable (contención). Con acumuladores
locales (sobrecarga de Parallel.For con localInit / body /
localFinally) cada hilo suma en su propia variable "subtotal" sin
lock y solo entra al lock UNA vez, al terminar su parte, en
localFinally. Pasamos de 30,000 bloqueos a unas pocas decenas.

En mis mediciones la mejora fue pequeña (86.8 ms -> 82.6 ms en el
mejor caso, entre 1 % y 6 % según el grado). Esto se debe a que cada
pedido hace 1,500 iteraciones con Sqrt y Sin (~20 microsegundos),
mientras que el lock dura apenas unos nanosegundos. Los hilos casi
nunca coinciden en el lock, así que la contención era baja desde el
principio. Si cada pedido fuera muy liviano (por ejemplo, una sola
suma), el lock por pedido dominaría el tiempo y la versión con
acumuladores locales sería muchas veces más rápida. Por eso es la
técnica recomendada: no depende de que el trabajo sea pesado.

La eficiencia de 108.7 % con grado 1 no significa que un hilo rinda
más de lo posible. Ese caso no paga ningún lock y la diferencia (~8 %)
está dentro de la variación normal entre ejecuciones (turbo de la CPU,
temperatura, procesos en segundo plano).


==================================================================
PREGUNTAS DE ANÁLISIS (ajustadas a mis resultados)
==================================================================

a) En mi equipo el grado 16 fue el más rápido, pero apenas mejor
   que el 8. ¿Por qué? ¿Y por qué en el ejemplo hipotético 8 superó
   a 16?
------------------------------------------------------------------
Mi CPU tiene 12 procesadores lógicos. Con grado 8 quedan 4 sin usar,
y con grado 16 se ocupan los 12, por eso 16 ganó un poco. Pero los 4
hilos que sobran por encima de 12 no tienen un procesador propio:
compiten por los mismos 12 y el sistema operativo tiene que alternar
entre ellos (cambios de contexto). Así que en la práctica 16 rinde
como 12.

Además, los 12 lógicos son en realidad 6 núcleos físicos con SMT.
Dos hilos en el mismo núcleo comparten sus unidades de cálculo de
punto flotante, y este programa es puro cálculo (Sqrt, Sin), así que
el segundo hilo de cada núcleo aporta mucho menos que un núcleo
completo. Por eso la curva se aplana después de 6-8 hilos.

En el ejemplo hipotético la máquina probablemente tenía 8
procesadores lógicos: con grado 16 había el doble de hilos que
procesadores, y el costo de alternarlos más la sincronización hizo
que el tiempo empeorara (650 ms contra 480 ms). Conclusión: el mejor
grado depende del hardware y suele estar cerca del número de
procesadores lógicos, no por encima.

b) ¿Por qué la eficiencia disminuye aunque la aceleración aumente?
------------------------------------------------------------------
Porque cada hilo nuevo aporta menos que el anterior. En mis datos, de
8 a 16 hilos el speedup sube de 6.33x a 6.95x, pero se duplicaron los
hilos: E = 6.95 / 16 = 43.4 %. Las causas son:
  - la parte que no se paraleliza (crear y repartir tareas, juntar
    resultados, el lock), que sigue costando lo mismo sin importar
    cuántos hilos haya (Ley de Amdahl);
  - SMT: el núcleo 7 al 12 no es un núcleo nuevo, sino la mitad de
    uno ya ocupado;
  - con más de 12 hilos ya no hay hardware nuevo, solo más hilos
    turnándose.
El tiempo total baja, pero cada hilo se aprovecha menos.

c) ¿Se puede garantizar que la computadora de otro estudiante
   obtendrá los mismos resultados?
------------------------------------------------------------------
El RESULTADO numérico sí (con una diferencia mínima en los últimos
decimales por el orden de las sumas), siempre que use lock o
acumuladores locales. Los TIEMPOS no. Dependen de:
  - la CPU: cantidad de núcleos físicos y lógicos, frecuencia, turbo,
    caché y si tiene SMT;
  - el estado del equipo: batería o corriente, plan de energía,
    temperatura (thermal throttling), programas abiertos, antivirus;
  - el software: versión de .NET, modo Debug o Release.
Incluso en mi propia computadora los tiempos varían de una ejecución
a otra; por eso repetí cada medición 3 veces y tomé la mediana.

d) Si el algoritmo tuviera un 10 % de trabajo secuencial, ¿cuál sería
   la aceleración máxima según Amdahl?
------------------------------------------------------------------
Ley de Amdahl:  S(p) = 1 / ( f + (1 - f) / p ),  con f = 0.10

  Con infinitos procesadores:  S_max = 1 / f = 1 / 0.10 = 10x
  Con mis 12 lógicos:  S(12) = 1 / (0.10 + 0.90/12) = 1 / 0.175 = 5.71x
  Con 32 núcleos:      S(32) = 1 / (0.10 + 0.90/32) = 1 / 0.128 = 7.80x

Aunque se compren infinitos núcleos, el programa nunca sería más de
10 veces más rápido, porque ese 10 % siempre se ejecuta en un solo
hilo.

Estimación con mis datos: si despejo f de Amdahl (métrica de
Karp-Flatt) con el grado 8 (S = 6.33x):
  f = (1/S - 1/p) / (1 - 1/p) = (0.158 - 0.125) / 0.875 ≈ 0.038
Es decir, mi programa se comporta como si tuviera ~3.8 % de parte
secuencial (incluye la sobrecarga de Parallel.For y el lock), lo que
limitaría el speedup teórico a ~1/0.038 ≈ 26x.

e) ¿Qué cambiaría si en lugar de procesar los mismos 30,000 pedidos
   aumentáramos la cantidad proporcionalmente al hardware?
   (Gustafson)
------------------------------------------------------------------
Amdahl mantiene el problema FIJO (30,000 pedidos) y pregunta cuánto
más rápido se termina; por eso choca con el límite 1/f. Gustafson
mantiene el TIEMPO fijo y pregunta cuánto MÁS trabajo se puede hacer
en ese tiempo si el problema crece con el hardware.

  Ley de Gustafson:  S(p) = p - f (p - 1),  con f = 0.10
    S(12) = 12 - 0.10 * 11 = 10.9x
    S(32) = 32 - 0.10 * 31 = 28.9x

Si con 12 hilos procesamos 12 x 30,000 = 360,000 pedidos en lugar de
30,000, la parte secuencial (crear tareas, juntar resultados) casi no
crece, mientras que la parte paralela (calcular pedidos) crece con
los hilos. La fracción secuencial se vuelve cada vez más pequeña en
proporción y la eficiencia se mantiene alta. Para la empresa esto es
lo realista: un servidor con más núcleos no sirve tanto para procesar
los mismos pedidos más rápido, sino para procesar muchos más pedidos
en el mismo tiempo.


==================================================================
PREGUNTA GENERAL
El servidor de 32 núcleos procesa apenas un poco más rápido.
¿Qué investigaría antes de recomendar uno aún más potente?
==================================================================
Primero mediría, no compraría. Investigaría:

1. Si el código realmente usa los 32 núcleos. Revisar si hay un
   MaxDegreeOfParallelism fijo (por ejemplo 4 u 8), si sigue
   corriendo la versión secuencial o si es una máquina virtual con
   menos vCPU asignadas. Mirar el Administrador de tareas: si la CPU
   no está cerca del 100 %, el problema no es la potencia.

2. Cuánto del proceso es secuencial. Medir con Stopwatch cada etapa
   (leer pedidos, validar, guardar) y calcular f. Según Amdahl, si
   f = 10 %, 32 núcleos dan como máximo 7.8x y 64 núcleos apenas
   8.8x: comprar otro servidor casi no ayudaría.

3. Contención por locks. Un lock en cada pedido o un recurso
   compartido (log, contador, conexión) hace que los 32 hilos hagan
   fila. Se resuelve con acumuladores locales o estructuras
   concurrentes, como en el paso 5.

4. Cuellos de botella fuera de la CPU. Si cada pedido espera a la
   base de datos, la red o el disco (trabajo I/O-bound), más núcleos
   no aceleran nada; ahí sirven async/await, consultas por lotes o
   mejorar la base de datos.

5. El hardware real. Cuántos de los 32 son núcleos físicos y cuántos
   lógicos (SMT), ancho de banda de memoria, arquitectura NUMA (dos
   procesadores físicos con memoria separada), frecuencia por núcleo
   (los servidores con muchos núcleos suelen tener menor frecuencia
   que una PC de escritorio).

6. El volumen de trabajo. Si hay pocos pedidos o cada uno es muy
   liviano, la sobrecarga de repartir tareas supera la ganancia.
   Según Gustafson, el servidor rinde mejor si se le da más volumen.

Solo si después de corregir esto la CPU está saturada, el código es
casi todo paralelo y el trabajo es de cálculo, tendría sentido un
servidor más potente. Si no, lo más barato y efectivo es optimizar el
software.
*/
