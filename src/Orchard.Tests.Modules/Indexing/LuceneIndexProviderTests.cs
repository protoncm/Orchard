﻿using System;
using System.IO;
using System.Linq;
using Autofac;
using Lucene.Services;
using NUnit.Framework;
using Orchard.Environment.Configuration;
using Orchard.FileSystems.AppData;
using Orchard.Indexing;
using Orchard.Indexing.Services;
using Orchard.Tests.FileSystems.AppData;

namespace Orchard.Tests.Modules.Indexing {
    public class LuceneIndexProviderTests {
        private IContainer _container;
        private IIndexProvider _provider;
        private IAppDataFolder _appDataFolder;
        private ShellSettings _shellSettings;
        private readonly string _basePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        
        [TestFixtureTearDown]
        public void Clean() {
            Directory.Delete(_basePath, true);
        }

        [SetUp]
        public void Setup() {
            if (Directory.Exists(_basePath)) {
                Directory.Delete(_basePath, true);
            }
            Directory.CreateDirectory(_basePath);

            _appDataFolder = AppDataFolderTests.CreateAppDataFolder(_basePath);

            var builder = new ContainerBuilder();
            builder.RegisterType<LuceneIndexProvider>().As<IIndexProvider>();
            builder.RegisterInstance(_appDataFolder).As<IAppDataFolder>();

            // setting up a ShellSettings instance
            _shellSettings = new ShellSettings { Name = "My Site" };
            builder.RegisterInstance(_shellSettings).As<ShellSettings>();

            _container = builder.Build();
            _provider = _container.Resolve<IIndexProvider>();
        }

        private string[] Indexes() {
            return new DirectoryInfo(Path.Combine(_basePath, "Sites", "My Site", "Indexes")).GetDirectories().Select(d => d.Name).ToArray();
        }

        [Test]
        public void IndexProviderShouldCreateNewIndex() {
            Assert.That(Indexes().Length, Is.EqualTo(0));

            _provider.CreateIndex("default");
            Assert.That(Indexes().Length, Is.EqualTo(1));
        }

        [Test]
        public void IndexProviderShouldOverwriteAlreadyExistingIndex() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(1).Add("body", null));
            Assert.That(_provider.IsEmpty("default"), Is.False);

            _provider.CreateIndex("default");
            Assert.That(_provider.IsEmpty("default"), Is.True);
        }

        [Test]
        public void IndexProviderShouldDeleteExistingIndex() {
            Assert.That(Indexes().Length, Is.EqualTo(0));

            _provider.CreateIndex("default");
            Assert.That(Indexes().Length, Is.EqualTo(1));

            _provider.DeleteIndex("default");
            Assert.That(Indexes().Length, Is.EqualTo(0));
        }

        [Test]
        public void IndexProviderShouldListExistingIndexes() {
            Assert.That(Indexes().Length, Is.EqualTo(0));
            
            _provider.CreateIndex("default");
            Assert.That(Indexes().Length, Is.EqualTo(1));
            Assert.That(Indexes()[0], Is.EqualTo("default"));

            _provider.CreateIndex("foo");
            Assert.That(Indexes().Length, Is.EqualTo(2));
        }

        [Test]
        public void ANewIndexShouldBeEmpty() {
            _provider.CreateIndex("default");
            var searchBuilder = _provider.CreateSearchBuilder("default");
            var hits = searchBuilder.Search();

            Assert.That(hits.Count(), Is.EqualTo(0));
        }

        [Test]
        public void DocumentsShouldBeSearchableById() {
            _provider.CreateIndex("default");

            _provider.Store("default", _provider.New(42));
            
            var searchBuilder = _provider.CreateSearchBuilder("default");

            var hit = searchBuilder.Get(42);
            Assert.IsNotNull(hit);
            Assert.That(hit.ContentItemId, Is.EqualTo(42));

            hit = searchBuilder.Get(1);
            Assert.IsNull(hit);
        }

        [Test]
        public void PropertiesShouldNotBeLost() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(42).Add("prop1", "value1").Store());

            var hit = _provider.CreateSearchBuilder("default").Get(42);
            
            Assert.IsNotNull(hit);
            Assert.That(hit.ContentItemId, Is.EqualTo(42));
            Assert.That(hit.GetString("prop1"), Is.EqualTo("value1"));
            
        }
        
        [Test]
        public void ShouldHandleMultipleIndexes() {
            _provider.CreateIndex("default1");
            _provider.Store("default1", _provider.New(1));

            _provider.CreateIndex("default2");
            _provider.Store("default2", _provider.New(2));

            _provider.CreateIndex("default3");
            _provider.Store("default3", _provider.New(3));

            Assert.IsNotNull(_provider.CreateSearchBuilder("default1").Get(1));
            Assert.IsNotNull(_provider.CreateSearchBuilder("default2").Get(2));
            Assert.IsNotNull(_provider.CreateSearchBuilder("default3").Get(3));

            Assert.IsNull(_provider.CreateSearchBuilder("default1").Get(2));
            Assert.IsNull(_provider.CreateSearchBuilder("default2").Get(3));
            Assert.IsNull(_provider.CreateSearchBuilder("default3").Get(1));

        }

        [Test]
        public void IdentifierShouldNotCollide() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(1).Add("field", "value1"));
            _provider.Store("default", _provider.New(11).Add("field", "value11"));
            _provider.Store("default", _provider.New(111).Add("field", "value111"));

            var searchBuilder = _provider.CreateSearchBuilder("default");

            Assert.That(searchBuilder.Get(1).ContentItemId, Is.EqualTo(1));
            Assert.That(searchBuilder.Get(11).ContentItemId, Is.EqualTo(11));
            Assert.That(searchBuilder.Get(111).ContentItemId, Is.EqualTo(111));
        }
        
        [Test]
        public void TagsShouldBeRemoved() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(1).Add("body", "<hr>some content</hr>").Analyze());
            _provider.Store("default", _provider.New(2).Add("body", "<hr>some content</hr>").RemoveTags().Analyze());

            var searchBuilder = _provider.CreateSearchBuilder("default");

            Assert.That(searchBuilder.WithField("body", "hr").Search().Count(), Is.EqualTo(1));
            Assert.That(searchBuilder.WithField("body", "hr").Search().First().ContentItemId, Is.EqualTo(1));
        }

        [Test] public void ShouldAllowNullOrEmptyStrings() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(1).Add("body", null));
            _provider.Store("default", _provider.New(2).Add("body", ""));
            _provider.Store("default", _provider.New(3).Add("body", "<hr></hr>").RemoveTags());

            var searchBuilder = _provider.CreateSearchBuilder("default");

            Assert.That(searchBuilder.Get(1).ContentItemId, Is.EqualTo(1));
            Assert.That(searchBuilder.Get(2).ContentItemId, Is.EqualTo(2));
            Assert.That(searchBuilder.Get(3).ContentItemId, Is.EqualTo(3));
        }

        [Test]
        public void ProviderShouldStoreSettings() {
            _provider.CreateIndex("default");
            Assert.That(_provider.GetLastIndexUtc("default"), Is.EqualTo(LuceneIndexProvider.DefaultMinDateTime));

            _provider.SetLastIndexUtc("default", new DateTime(2010, 1, 1, 1, 1, 1, 1));
            Assert.That(_provider.GetLastIndexUtc("default"), Is.EqualTo(new DateTime(2010, 1, 1, 1, 1, 1, 0)));

            _provider.SetLastIndexUtc("default", new DateTime(1901, 1, 1, 1, 1, 1, 1));
            Assert.That(_provider.GetLastIndexUtc("default"), Is.EqualTo(LuceneIndexProvider.DefaultMinDateTime));
        }

        [Test]
        public void IsEmptyShouldBeTrueForNoneExistingIndexes() {
            _provider.IsEmpty("dummy");
            Assert.That(_provider.IsEmpty("default"), Is.True);
        }

        [Test]
        public void IsEmptyShouldBeTrueForJustNewIndexes() {
            _provider.CreateIndex("default");
            Assert.That(_provider.IsEmpty("default"), Is.True);
        }

        [Test]
        public void IsEmptyShouldBeFalseWhenThereIsADocument() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(1).Add("body", null));
            Assert.That(_provider.IsEmpty("default"), Is.False);
        }

        [Test]
        public void IsDirtyShouldBeFalseForNewDocuments() {
            IDocumentIndex doc = _provider.New(1);
            Assert.That(doc.IsDirty, Is.False);
        }


        [Test]
        public void IsDirtyShouldBeTrueWhenIndexIsModified() {
            IDocumentIndex doc;
            
            doc = _provider.New(1);
            doc.Add("foo", "value");
            Assert.That(doc.IsDirty, Is.True);

            doc = _provider.New(1);
            doc.Add("foo", false);
            Assert.That(doc.IsDirty, Is.True);

            doc = _provider.New(1);
            doc.Add("foo", (float)1.0);
            Assert.That(doc.IsDirty, Is.True);

            doc = _provider.New(1);
            doc.Add("foo", 1);
            Assert.That(doc.IsDirty, Is.True);

            doc = _provider.New(1);
            doc.Add("foo", DateTime.Now);
            Assert.That(doc.IsDirty, Is.True);

        }

        [Test]
        public void DocumentsShouldBeDeleted() {
            _provider.CreateIndex("default");
            _provider.Store("default", _provider.New(1).Add("field", "value1"));
            _provider.Store("default", _provider.New(11).Add("field", "value11"));
            _provider.Store("default", _provider.New(111).Add("field", "value111"));

            var searchBuilder = _provider.CreateSearchBuilder("default");

            Assert.That(searchBuilder.Get(1).ContentItemId, Is.EqualTo(1));
            Assert.That(searchBuilder.Get(11).ContentItemId, Is.EqualTo(11));
            Assert.That(searchBuilder.Get(111).ContentItemId, Is.EqualTo(111));

            _provider.Delete("default", 1);

            Assert.That(searchBuilder.Get(1), Is.Null);
            Assert.That(searchBuilder.Get(11).ContentItemId, Is.EqualTo(11));
            Assert.That(searchBuilder.Get(111).ContentItemId, Is.EqualTo(111));

            _provider.Delete("default", new int[] {1, 11, 111 });

            Assert.That(searchBuilder.Get(1), Is.Null);
            Assert.That(searchBuilder.Get(11), Is.Null);
            Assert.That(searchBuilder.Get(111), Is.Null);

        }

        [Test]
        public void SameContentItemShouldNotBeIndexedTwice() {
            _provider.CreateIndex("default");

            var searchBuilder = _provider.CreateSearchBuilder("default");

            _provider.Store("default", _provider.New(1).Add("field", "value1"));
            Assert.That(searchBuilder.WithField("id", "1").Count(), Is.EqualTo(1));

            _provider.Store("default", _provider.New(1).Add("field", "value2"));
            Assert.That(searchBuilder.WithField("id", "1").Count(), Is.EqualTo(1));
        }
    }
}
