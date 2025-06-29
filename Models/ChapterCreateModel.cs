﻿using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace BulbaLib.Models
{
    public class ChapterCreateModel
    {
        [Required]
        public int NovelId { get; set; }

        [Required(ErrorMessage = "Пожалуйста, укажите номер главы (например, 'Глава 1' или 'Том 1 Глава 1').")]
        [StringLength(100, ErrorMessage = "Номер главы должен быть до 100 символов.")]
        public string Number { get; set; }

        [Required(ErrorMessage = "Пожалуйста, введите название главы.")]
        [StringLength(200, ErrorMessage = "Название главы должно быть до 200 символов.")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Пожалуйста, введите содержимое главы.")]
        [DataType(DataType.MultilineText)]
        public string Content { get; set; }

        public string? NovelTitle { get; set; }

        // ChapterTextFile НЕ обязателен, нужен только для клиента
        //public IFormFile? ChapterTextFile { get; set; }

        // Используется для передачи ID созданного черновика главы, если он был создан до основного сохранения
        public int? DraftChapterId { get; set; }
    }
}