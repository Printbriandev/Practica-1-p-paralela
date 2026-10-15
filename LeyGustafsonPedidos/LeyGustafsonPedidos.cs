/*
 * ==================================================================
 *   Instituto Tecnológico de Las Américas (ITLA)
 *   Programación Paralela
 *
 *   Práctica de Laboratorio 2
 *   Ley de Gustafson y Análisis de Rendimiento Concurrente
 *
 *   Estudiante : Brian Amaury Guzman Mañon
 *   Matrícula  : 2024-1286
 *   Docente    : Erick Leonardo Perez Veloz
 *   Fecha      : 14 de octubre de 2026
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
    // Carga de referencia (paso 1) y costo de cada pedido (igual que en el Laboratorio 1)
    const int PedidosBase = 100000;
    const int Iteraciones = 1500;

    // El tiempo de cada escenario debe quedar dentro de T_referencia +/- 10 %
    const double Tolerancia = 0.10;

    // Intentos maximos para ajustar la cantidad de pedidos de un grado
    const int MaxIntentos = 6;

    // Las cantidades de pedidos se redondean a multiplos de 10,000
    const int Redondeo = 10000;

    // Cuantas veces se repite cada medicion (se reporta la mediana)
    const int Repeticiones = 5;

    // Fraccion secuencial supuesta en el enunciado para la formula de Gustafson
    const double FraccionSecuencialSupuesta = 0.05;

    static readonly int[] GradosParalelismo = { 1, 2, 4, 8, 16 };

    static readonly object candado = new object();

    // ------------------------------------------------------------
    // Trabajo de cada pedido (mismo calculo del Laboratorio 1)
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
    // Procesamiento paralelo con acumuladores locales.
    // Es la version que mejor rindio en el Laboratorio 1: cada hilo suma
    // en su propio subtotal y solo usa el lock una vez al terminar.
    // Ahora recibe la cantidad de pedidos, porque en Gustafson la carga crece.
    // ------------------------------------------------------------
    static double ProcesarParalelo(int totalPedidos, int hilos)
    {
        double total = 0;

        ParallelOptions opciones = new ParallelOptions
        {
            MaxDegreeOfParallelism = hilos
        };

        Parallel.For(
            0, totalPedidos, opciones,
            () => 0.0,
            (i, estado, subtotal) => subtotal + ProcesarPedido(i),
            subtotal =>
            {
                lock (candado)
                {
                    total += subtotal;
                }
            });

        return total;
    }

    // Ejecuta varias veces y devuelve la mediana del tiempo en segundos.
    static double MedirSegundos(int totalPedidos, int hilos)
    {
        double[] tiempos = new double[Repeticiones];

        for (int r = 0; r < Repeticiones; r++)
        {
            Stopwatch reloj = Stopwatch.StartNew();
            ProcesarParalelo(totalPedidos, hilos);
            reloj.Stop();
            tiempos[r] = reloj.Elapsed.TotalSeconds;
        }

        Array.Sort(tiempos);
        return tiempos[Repeticiones / 2];
    }

    static int RedondearPedidos(double pedidos)
    {
        int redondeado = (int)Math.Round(pedidos / Redondeo) * Redondeo;
        return Math.Max(Redondeo, redondeado);
    }

    // Resultado final de cada grado de paralelismo
    struct Escenario
    {
        public int Grado;
        public int Pedidos;
        public double Tiempo;
        public bool DentroDeTolerancia;
    }

    // ------------------------------------------------------------
    // Paso 2: buscar cuantos pedidos se procesan en un tiempo similar
    // a la referencia. Se empieza con la hipotesis ideal (base * p) y,
    // si el tiempo se sale de la tolerancia, se corrige con regla de tres:
    // como el tiempo es proporcional a los pedidos,
    //     pedidos_nuevos = pedidos * T_referencia / T_medido
    // ------------------------------------------------------------
    static Escenario Calibrar(int grado, double tiempoReferencia)
    {
        int pedidos = PedidosBase * grado;
        Escenario mejor = new Escenario { Grado = grado };
        double mejorDesviacion = double.MaxValue;

        Console.WriteLine();
        Console.WriteLine($"Grado {grado}: hipotesis ideal = {pedidos:N0} pedidos");

        for (int intento = 1; intento <= MaxIntentos; intento++)
        {
            double tiempo = MedirSegundos(pedidos, grado);
            double desviacion = (tiempo - tiempoReferencia) / tiempoReferencia;
            bool dentro = Math.Abs(desviacion) <= Tolerancia;

            Console.WriteLine(
                $"  Intento {intento}: {pedidos,9:N0} pedidos -> {tiempo,6:F2} s  ({desviacion,8:+0.00%;-0.00%})  {(dentro ? "DENTRO de +/-10%" : "fuera")}"
            );

            if (Math.Abs(desviacion) < mejorDesviacion)
            {
                mejorDesviacion = Math.Abs(desviacion);
                mejor.Pedidos = pedidos;
                mejor.Tiempo = tiempo;
                mejor.DentroDeTolerancia = dentro;
            }

            if (dentro)
            {
                break;
            }

            int siguiente = RedondearPedidos(pedidos * tiempoReferencia / tiempo);
            if (siguiente == pedidos)
            {
                break; // el redondeo ya no permite acercarse mas
            }
            pedidos = siguiente;
        }

        return mejor;
    }

    static void Main()
    {
        Console.WriteLine("=== LEY DE GUSTAFSON: MAS PEDIDOS EN LA MISMA VENTANA DE TIEMPO ===");
        Console.WriteLine($"Procesadores logicos: {Environment.ProcessorCount}");
        Console.WriteLine($"Iteraciones por pedido: {Iteraciones:N0} | Tolerancia: +/-{Tolerancia:P0} | Mediana de {Repeticiones} ejecuciones");

        // Calentamiento del compilador JIT
        ProcesarParalelo(10000, 1);
        ProcesarParalelo(10000, Environment.ProcessorCount);

        // ---------- Paso 1: referencia ----------
        double tiempoReferencia = MedirSegundos(PedidosBase, 1);
        Console.WriteLine();
        Console.WriteLine(
            $"Referencia: {PedidosBase:N0} pedidos con grado 1 -> {tiempoReferencia:F2} s"
        );
        Console.WriteLine(
            $"Ventana aceptada: {tiempoReferencia * (1 - Tolerancia):F2} s a {tiempoReferencia * (1 + Tolerancia):F2} s"
        );

        // ---------- Paso 2: calibracion por grado ----------
        Escenario[] resultados = new Escenario[GradosParalelismo.Length];
        resultados[0] = new Escenario
        {
            Grado = 1,
            Pedidos = PedidosBase,
            Tiempo = tiempoReferencia,
            DentroDeTolerancia = true
        };

        for (int k = 1; k < GradosParalelismo.Length; k++)
        {
            resultados[k] = Calibrar(GradosParalelismo[k], tiempoReferencia);
        }

        // Se repite la referencia al final: si la CPU se calento o el sistema
        // cambio de estado durante la prueba, este valor se aleja del inicial.
        double referenciaFinal = MedirSegundos(PedidosBase, 1);
        double deriva = (referenciaFinal - tiempoReferencia) / tiempoReferencia;
        Console.WriteLine();
        Console.WriteLine(
            $"Referencia repetida al final: {referenciaFinal:F2} s (variacion {deriva:+0.00%;-0.00%} respecto al inicio)"
        );

        // ---------- Tabla final ----------
        // C          = N_p / N_1                    (factor de capacidad)
        // C / p      = que tan cerca del ideal escalo cada trabajador
        // Throughput = pedidos por segundo
        // S_G        = p - f (p - 1)                (Gustafson con f = 5 %)
        // f empirica = (p - C) / (p - 1)            (se despeja f usando C medido)
        Console.WriteLine();
        Console.WriteLine("=== RESUMEN ===");
        Console.WriteLine("Grado |   Pedidos | Tiempo  |     C  |     C/p | Pedidos/s | Gustafson f=5% | f empirica");
        Console.WriteLine("------+-----------+---------+--------+---------+-----------+----------------+-----------");

        foreach (Escenario e in resultados)
        {
            int p = e.Grado;
            double capacidad = (double)e.Pedidos / PedidosBase;
            double throughput = e.Pedidos / e.Tiempo;
            double gustafson = p - FraccionSecuencialSupuesta * (p - 1);
            string fEmpirica = p == 1 ? "        -" : $"{(p - capacidad) / (p - 1),9:P1}";

            Console.WriteLine(
                $"{p,5} | {e.Pedidos,9:N0} | {e.Tiempo,5:F2} s | {capacidad,5:F2}x | {capacidad / p,7:P1} | {throughput,9:N0} | {gustafson,13:F2}x | {fEmpirica}{(e.DentroDeTolerancia ? "" : "  (fuera de tolerancia)")}"
            );
        }
    }
}

/*Mi análisis

==================================================================
RESULTADOS OBTENIDOS (AMD Ryzen 5 7535HS, 12 procesadores lógicos)
Cada tiempo es la mediana de 5 ejecuciones en modo Release.
==================================================================

Paso 1: referencia
  100,000 pedidos con grado 1 -> 1.76 s
  Ventana de tiempo aceptada (±10 %): 1.58 s a 1.93 s
  Referencia repetida al final de la prueba: 1.77 s (+0.63 %),
  así que el equipo se mantuvo estable durante toda la medición.

Paso 2: búsqueda de la carga que cabe en la misma ventana
  Se empezó con la hipótesis ideal (100,000 x p). Si el tiempo se salía
  de ±10 %, el programa corregía con una regla de tres
  (pedidos x T_referencia / T_medido) y volvía a medir.

  Grado | Hipótesis ideal         | Ajuste que cayó en la ventana
  ------+-------------------------+------------------------------
    2   |   200,000 -> 2.15 s ✗   |   160,000 -> 1.70 s (-2.97 %)
    4   |   400,000 -> 2.26 s ✗   |   310,000 -> 1.74 s (-1.07 %)
    8   |   800,000 -> 2.57 s ✗   |   550,000 -> 1.80 s (+2.77 %)
   16   | 1,600,000 -> 4.36 s ✗   |   640,000 -> 1.73 s (-1.36 %)

  Ninguna hipótesis ideal se cumplió: en todos los casos el equipo
  tardó más que la ventana. Como advertía el enunciado, el escalamiento
  ideal no es el resultado esperado.

Tabla final

  Factor de capacidad       C = N_p / N_1
  Escalamiento por trabajador = C / p
  Gustafson                 S(p) = p - f (p - 1),  con f = 0.05

  Grado | Pedidos  | Tiempo | C     | C/p    | Pedidos/s | Gustafson f=5%
  ------+----------+--------+-------+--------+-----------+---------------
    1   | 100,000  | 1.76 s | 1.00x | 100.0% |   56,949  |   1.00x
    2   | 160,000  | 1.70 s | 1.60x |  80.0% |   93,909  |   1.95x
    4   | 310,000  | 1.74 s | 3.10x |  77.5% |  178,460  |   3.85x
    8   | 550,000  | 1.80 s | 5.50x |  68.8% |  304,770  |   7.65x
   16   | 640,000  | 1.73 s | 6.40x |  40.0% |  369,490  |  15.25x

Capacidad medida vs. modelo de Gustafson (f = 5 %)

  Pedidos en ~1.75 s
  16x |                                         G (15.25x)
      |
   8x |                         G (7.65x)
      |                         * (5.50x)                 * (6.40x)
   4x |           G (3.85x)
      |           * (3.10x)
   2x |     G * (1.95x / 1.60x)
   1x | *
      +-----+-----+-----------+-----------------------+---- Grado
            1     2     4           8                      16
   * = medido    G = Gustafson con f = 5 %

  Hasta grado 8 la capacidad medida sigue la forma del modelo, aunque
  por debajo. De 8 a 16 el modelo se duplica, pero la medición solo
  sube de 5.50x a 6.40x, porque el equipo tiene 12 procesadores
  lógicos y no 16.

Interpretación del factor de capacidad
  Con grado 4 el sistema procesó 3.10 veces más pedidos en la misma
  ventana de tiempo. Gustafson con f = 5 % predice 3.85x. La diferencia
  existe porque la comparación no es exacta:
   - MaxDegreeOfParallelism = 4 es un LÍMITE de concurrencia, no una
     garantía de 4 núcleos físicos dedicados. Windows decide dónde
     corre cada hilo, y dos de ellos pueden caer en el mismo núcleo
     físico (SMT), compartiendo sus unidades de cálculo.
   - El turbo de la CPU: con un solo núcleo trabajando, el Ryzen sube
     a su frecuencia máxima. Con varios núcleos activos, la frecuencia
     baja para no pasarse de temperatura y consumo. Por eso la
     referencia de un hilo es "más rápida por hilo" que cualquier
     escenario paralelo. Eso explica que el grado 2 solo logre 1.60x.
   - El 5 % es un supuesto del enunciado, no un dato del experimento.

  Si despejo f de la fórmula con mis datos, f = (p - C) / (p - 1):
     p = 4  -> f = (4 - 3.10) / 3  = 30.0 %
     p = 8  -> f = (8 - 5.50) / 7  = 35.7 %
     p = 16 -> f = (16 - 6.40) / 15 = 64.0 %
  Estos valores NO son una parte secuencial real del código: cada
  pedido es independiente y Parallel.For solo agrega una sobrecarga
  mínima. Son pérdidas del hardware (turbo, SMT, 12 lógicos en lugar
  de 16) que la fórmula, al ser tan simple, contabiliza como si fueran
  trabajo secuencial. Esto confirma la advertencia del enunciado: la
  medición empírica no debe confundirse con el speedup teórico.

Nota sobre la metodología
  En corridas de prueba anteriores, algunas mediciones salieron
  inconsistentes (por ejemplo, 330,000 pedidos tardaron más que
  400,000), porque había interferencia de otros programas (OneDrive
  sincronizando, aplicaciones abiertas). Por eso el programa usa la
  mediana de 5 ejecuciones y repite la referencia al final: si la
  variación entre el inicio y el final fuera grande, las mediciones no
  serían comparables. En la corrida final fue de solo +0.63 %.


==================================================================
PREGUNTAS DE ANÁLISIS
==================================================================

1. ¿Por qué no pudimos multiplicar indefinidamente los pedidos
   manteniendo el mismo tiempo?
------------------------------------------------------------------
Porque el hardware es finito. La ley de Gustafson supone que cada
trabajador nuevo es un procesador real que aporta la misma potencia,
y en mi equipo eso deja de cumplirse muy pronto:

 - Solo hay 12 procesadores lógicos, y en realidad son 6 núcleos
   físicos con SMT. Los hilos 7 a 12 comparten núcleo con otros y
   aportan mucho menos que un núcleo completo, porque este cálculo
   (Sqrt, Sin) ocupa las mismas unidades de punto flotante.
 - Con más núcleos activos, la CPU baja su frecuencia (turbo) por
   temperatura y consumo, así que cada hilo rinde menos.
 - Por encima de 12 trabajadores no hay hardware nuevo: los hilos de
   más solo se turnan.

La evidencia está en la hipótesis de grado 16: 1,600,000 pedidos
tardaron 4.36 s, 2.5 veces la ventana. Lo máximo que cupo en ~1.75 s
fue 640,000. Una vez que el procesador está saturado, cada pedido
extra se suma directamente al tiempo total.

2. ¿Qué sucede si utilizamos 16 trabajadores en una computadora con
   4 núcleos disponibles?
------------------------------------------------------------------
Solo 4 hilos pueden ejecutarse al mismo tiempo. Los otros 12 esperan
su turno, y el sistema operativo los alterna (cambios de contexto).
La capacidad máxima sería cercana a 4x, nunca 16x. En la práctica
sería algo menos, como mi grado 4 (3.10x), porque los cambios de
contexto, la caché que se "ensucia" al cambiar de hilo y la
coordinación agregan costo sin agregar potencia.

Mi propio experimento muestra el mismo efecto a mayor escala: pedí 16
trabajadores en un equipo de 12 lógicos. El modelo esperaba 15.25x y
obtuve 6.40x, y la eficiencia por trabajador cayó a 40 %. Más
trabajadores que núcleos no aumentan la capacidad: solo reparten el
mismo hardware entre más hilos. Lo correcto es configurar
MaxDegreeOfParallelism cerca de Environment.ProcessorCount.

3. ¿Qué diferencia hay entre aumentar el speedup y aumentar el
   throughput?
------------------------------------------------------------------
 - Speedup: el MISMO trabajo se termina en MENOS tiempo. Mide
   latencia y corresponde a la Ley de Amdahl. En el Laboratorio 1,
   los mismos 30,000 pedidos pasaron de 603.7 ms a 86.8 ms (6.95x).
 - Throughput: se procesa MÁS trabajo en el MISMO tiempo. Mide
   pedidos por segundo y corresponde a la Ley de Gustafson. En este
   laboratorio la ventana se mantuvo en ~1.75 s y el throughput pasó
   de 56,949 a 369,490 pedidos/s (6.49x).

Los números se parecen porque los dos dependen de cuánto trabajo
útil hace el hardware por segundo, pero responden preguntas
distintas. Un cliente no ve su pedido procesado más rápido cuando
sube el throughput: cada pedido sigue tardando lo mismo en calcularse.
Lo que cambia es que la empresa atiende a muchos más clientes en el
mismo tiempo. Si el problema es que una respuesta tarda demasiado, se
necesita speedup; si el problema es que la demanda crece, se necesita
throughput.

4. ¿En qué situación empresarial usarías Gustafson?
------------------------------------------------------------------
Cuando el TIEMPO es fijo y lo que crece es el VOLUMEN, por ejemplo:

 - El cierre nocturno de esta tienda: todos los pedidos del día deben
   validarse entre las 00:00 y las 02:00. Si las ventas se triplican
   en temporada alta (Black Friday, Navidad), la ventana no se amplía;
   se necesita procesar el triple en las mismas dos horas.
 - Un banco que debe calcular el riesgo de fraude de todas las
   transacciones del día antes de abrir, cuando cada año hay más
   clientes.
 - La facturación mensual de una empresa de telecomunicaciones o
   electricidad, que debe salir en una fecha fija aunque crezca la
   base de clientes.

En esos casos Gustafson ayuda a planificar la capacidad: con las
mediciones de mi equipo sé que, con grado 8, entran ~550,000 pedidos
por ventana. Con eso se puede estimar cuántos servidores hacen falta
para el volumen esperado, siempre midiendo en el hardware real, porque
el escalamiento no es perfecto. En cambio, si el objetivo fuera que
un solo pedido responda más rápido (por ejemplo, aprobar un pago en
línea en menos de un segundo), el análisis correcto sería Amdahl.
*/
