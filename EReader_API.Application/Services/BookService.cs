using EReader_API.Application.Interfaces;
using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace EReader_API.Application.Services
{
    public class BookService : IBookService
    {
        private IBookRepository _bookRepository;

        public BookService(IBookRepository bookRepository)
        {
            _bookRepository = bookRepository;
        }

        public Task Add(Book bookDTO)
        {

        }

        public Task<Book> GetById(int? id)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<Book>> GetCategories()
        {
            throw new NotImplementedException();
        }

        public Task Remove(int? id)
        {
            throw new NotImplementedException();
        }

        public Task Update(Book bookDTO)
        {
            throw new NotImplementedException();
        }
    }
}
