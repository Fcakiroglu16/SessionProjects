using System.Collections.Concurrent;
using SessionProjects.Contracts.Dtos;

namespace Microservice2.API.Products;

// Demo amaçlı in-memory ürün deposu
public class ProductRepository
{
    private readonly ConcurrentDictionary<int, ProductDto> _products = new(new Dictionary<int, ProductDto>
    {
        [1] = new(1, "Kalem", 25.50m, 100),
        [2] = new(2, "Defter", 60m, 50),
        [3] = new(3, "Silgi", 10m, 200),
        [4] = new(4, "Deri Ajanda", 450m, 0),
        [5] = new(5, "Dolma Kalem", 2000m, 5)
    });

    public IReadOnlyList<ProductDto> GetAll() => _products.Values.OrderBy(p => p.Id).ToList();

    public ProductDto? GetById(int id) => _products.GetValueOrDefault(id);

    public bool DecreaseStock(int id, int quantity)
    {
        while (_products.TryGetValue(id, out var product))
        {
            if (product.Stock < quantity) return false;
            if (_products.TryUpdate(id, product with { Stock = product.Stock - quantity }, product)) return true;
        }

        return false;
    }
}
