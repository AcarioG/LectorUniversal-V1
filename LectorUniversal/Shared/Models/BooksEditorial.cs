﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LectorUniversal.Shared
{
    public class BooksEditorial
    {
        public int BookId { get; set; }
        public int EditorialId { get; set; }
        public Book? Book { get; set; }
        public Editorial? Editorial { get; set; }
    }
}
