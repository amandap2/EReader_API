using EReader_API.Domain.Entities.Catalog;
using EReader_API.Domain.Interfaces;
using EReader_API.Infra.Context;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace EReader_API.Infra.Repositories
{
    public class BookRepository : IBookRepository
    {
        ApplicationDbContext _bookContext;

        public BookRepository(ApplicationDbContext bookContext)
        {
            _bookContext = bookContext;
        }

        public async Task<Book> CreateAsync(Book book)
        {
            _bookContext.Add(book);
            await _bookContext.SaveChangesAsync();
            return book;
        }

        public async Task<IEnumerable<Book>> GetBooksAsync()
        {
            return await _bookContext.Books.ToListAsync();
        }

        public async Task<Book> GetByIdAsync(int? id)
        {
            return await _bookContext.Books.FindAsync(id);
        }

        public async Task<Book> RemoveAsync(Book book)
        {
            _bookContext.Remove(book);
            await _bookContext.SaveChangesAsync();
            return book;
        }

        public async Task<Book> UpdateAsync(Book book)
        {
            _bookContext.Update(book);
            await _bookContext.SaveChangesAsync();
            return book;
        }
    }
}
