using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.ML;

namespace Drivee_Model
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.ML;

    namespace Drivee_Model
    {
        public partial class Drivee_Model
        {
            public const string RetrainFilePath = @"C:\Users\dreamgonewithout\Downloads\Telegram Desktop\train.csv";
            public const char RetrainSeparatorChar = ',';
            public const bool RetrainHasHeader = true;

            public static void PreprocessDataAndTrainModel(string outputModelPath)
            {
                // Загрузка данных
                var data = LoadData(RetrainFilePath);

                // Предварительная обработка данных: расчет риска и золотой середины
                var processedData = CalculateRiskAndPrice(data);

                // Обучение модели на обработанных данных
                TrainModel(processedData, outputModelPath);
            }

            private static List<OrderData> LoadData(string filePath)
            {
                var data = new List<OrderData>();
                var lines = File.ReadAllLines(filePath);
                foreach (var line in lines.Skip(1))  // Пропускаем заголовок
                {
                    var columns = line.Split(RetrainSeparatorChar);

                    // Если цена пассажира или водителя 0, пропускаем строку
                    if (double.TryParse(columns[0], out double priceStartLocal) &&
                        double.TryParse(columns[1], out double priceBidLocal) &&
                        double.TryParse(columns[3], out double driverRating))
                    {
                        data.Add(new OrderData
                        {
                            PriceStartLocal = priceStartLocal,
                            PriceBidLocal = priceBidLocal,
                            IsDone = columns[2],
                            DriverRating = driverRating
                        });
                    }
                }
                return data;
            }

            private static List<OrderData> CalculateRiskAndPrice(List<OrderData> data)
            {
                foreach (var order in data)
                {
                    // Расчет золотой середины (среднее между ценой пассажира и водителя)
                    order.GoldenMiddlePrice = (order.PriceStartLocal + order.PriceBidLocal) / 2;

                    // Расчет риска
                    order.RiskFactor = CalculateRiskFactor(order.DriverRating, order.IsDone);
                }
                return data;
            }

            private static double CalculateRiskFactor(double driverRating, string isDone)
            {
                // Риск увеличивается, если заказ отменен
                double risk = (isDone == "cancel") ? 1.0 : 0.0;

                // Рейтинг водителя уменьшает риск (чем выше рейтинг, тем меньше риск)
                return risk - (driverRating / 5.0);
            }

            private static void TrainModel(List<OrderData> data, string outputModelPath)
            {
                var context = new MLContext();
                var trainData = context.Data.LoadFromEnumerable(data);

                // Обучение модели с использованием регрессии
                var pipeline = context.Regression.Trainers.Sdca(labelColumnName: "PriceStartLocal", featureColumnName: "GoldenMiddlePrice");
                var model = pipeline.Fit(trainData);

                // Сохраняем модель
                context.Model.Save(model, trainData.Schema, outputModelPath);
            }
        }

        
        public class OrderData
        {
            public double PriceStartLocal { get; set; }
            public double PriceBidLocal { get; set; }
            public string IsDone { get; set; }
            public double DriverRating { get; set; }
            public double GoldenMiddlePrice { get; set; }
            public double RiskFactor { get; set; }
        }
    }

}