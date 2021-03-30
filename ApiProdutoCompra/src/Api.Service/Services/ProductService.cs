using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Api.Domain.Dtos.Products;
using Api.Domain.Entities;
using Api.Domain.Interfaces;
using Api.Domain.Interfaces.Services.Products;
using Api.Domain.Models;
using AutoMapper;
using Newtonsoft.Json;

namespace Api.Service.Services
{
    public class ProductService : IProductService
    {
        private const string _comprasUrl = "http://localhost:5000/api/pagamento/compras";
        private static HttpClient _httpClient;
        private static HttpClient HttpClient => _httpClient ?? (_httpClient = new HttpClient());
        private IRepository<ProductEntity> _productrepository;
        private readonly IMapper _mapper;
        private IError _error;

        public ProductService(IRepository<ProductEntity> productrepository, IMapper mapper, IError error)
        {
            _productrepository = productrepository;
            _mapper = mapper;
            _error = error;
        }
        public async Task<bool> Delete(Guid id)
        {
            return await _productrepository.DeleteAsync(id);
        }

        public async Task<ProductDto> Get(Guid id)
        {
            var entity = await _productrepository.SelectAsync(id);
            return _mapper.Map<ProductDto>(entity);
        }

        public async Task<IEnumerable<ProductDto>> GetAll()
        {
            var listEntity = await _productrepository.SelectAsync();
            return _mapper.Map<IEnumerable<ProductDto>>(listEntity);
        }

        public async Task<ProductDtoCreateResult> Post(ProductDtoCreate product)
        {
            var model = _mapper.Map<ProductModel>(product);
            var entity = _mapper.Map<ProductEntity>(model);
            var result = await _productrepository.InsertAsync(entity);

            return _mapper.Map<ProductDtoCreateResult>(result);
        }
        public async Task<ProductDtoUpdateResult> Put(ProductDtoUpdate product)
        {
            var model = _mapper.Map<ProductModel>(product);
            var entity = _mapper.Map<ProductEntity>(model);
            var result = await _productrepository.UpdateAsync(entity);
            return _mapper.Map<ProductDtoUpdateResult>(result);
        }
        public async Task<RequestExternoDto> RequestExterno(PagamentoDto pagamento)
        {
            if (pagamento.Cartao.cvv.Length != 3 || pagamento.Cartao.numero.Length != 16)
            {
                _error.ErrorMessages.Add(new ErrorMessage
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                    Message = "Os valores informados não são válidos"
                });
            }
            var bandeiracartao = new List<string> { "VISA", "Mastercard", "AMEX", "Diners Club", "Discover", "Hipercard", "Inter" };
            if (!bandeiracartao.Contains(pagamento.Cartao.bandeira))
            {
                _error.ErrorMessages.Add(new ErrorMessage
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                    Message = "Não aceitamos essa bandeira"
                });
            }
            string[] date = pagamento.Cartao.data_expiracao.Split('/');
            if (int.Parse(date[1]) < DateTime.Now.Year)
            {
                _error.ErrorMessages.Add(new ErrorMessage
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                    Message = "Data do Cartão Expirada"
                });
            }
            if (int.Parse(date[0]) < DateTime.Now.Month && int.Parse(date[1]) == DateTime.Now.Month)
            {
                _error.ErrorMessages.Add(new ErrorMessage
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                    Message = "Data do Cartão Expirada"
                });
            }
            var getproduct = await Get(pagamento.produto_id);
            if (getproduct.qtde_estoque < pagamento.qtde_comprada)
            {
                _error.ErrorMessages.Add(new ErrorMessage
                {
                    StatusCode = HttpStatusCode.PreconditionFailed,
                    Message = "Quantidade Insuficiente no estoque"
                });
            }
            var prepararpagamento = pagamento.qtde_comprada * getproduct.valor_unitario;
            var pagamentosend = new PagamentoExternoSend
            {
                valor = prepararpagamento,
                cartao = pagamento.Cartao
            };
            var data = JsonConvert.SerializeObject(pagamentosend);
            var content = new StringContent(data, Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(_comprasUrl, content);
            var retorno = JsonConvert.DeserializeObject<RequestExternoDto>(response.Content.ReadAsStringAsync().Result);
            if (retorno.estado == "Aprovado")
            {
                getproduct.DataUltCompra = DateTime.UtcNow;
                getproduct.ValorUltVenda = retorno.valor;
                getproduct.qtde_estoque = getproduct.qtde_estoque - pagamento.qtde_comprada;
                var model = _mapper.Map<ProductModel>(getproduct);
                var entity = _mapper.Map<ProductEntity>(model);
                await _productrepository.UpdateAsync(entity);
            }
            return retorno;
        }
    }
}
