using EReader_API.Domain.Entities.Catalog;
using System;
using System.Collections.Generic;
using System.Text;

namespace EReader_API.Application.Interfaces
{
    public interface IBookService
    {
        Task<IEnumerable<Book>> GetCategories();
        Task<Book> GetById(int? id);
        Task Add(Book bookDTO);
        Task Update(Book bookDTO);
        Task Remove(int? id);
    }
}
