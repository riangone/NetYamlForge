PRAGMA foreign_keys = ON;

DROP TABLE IF EXISTS OrderDetails;
DROP TABLE IF EXISTS Orders;
DROP TABLE IF EXISTS Products;
DROP TABLE IF EXISTS Categories;
DROP TABLE IF EXISTS Suppliers;
DROP TABLE IF EXISTS Shippers;
DROP TABLE IF EXISTS Employees;
DROP TABLE IF EXISTS Customers;

CREATE TABLE Customers (
  CustomerId INTEGER PRIMARY KEY AUTOINCREMENT,
  CompanyName TEXT NOT NULL,
  ContactName TEXT NOT NULL,
  Country TEXT NOT NULL,
  City TEXT,
  Phone TEXT,
  Active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE Employees (
  EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT,
  LastName TEXT NOT NULL,
  FirstName TEXT NOT NULL,
  Title TEXT,
  Region TEXT,
  HireDate TEXT
);

CREATE TABLE Shippers (
  ShipperId INTEGER PRIMARY KEY AUTOINCREMENT,
  CompanyName TEXT NOT NULL,
  Phone TEXT
);

CREATE TABLE Suppliers (
  SupplierId INTEGER PRIMARY KEY AUTOINCREMENT,
  CompanyName TEXT NOT NULL,
  ContactName TEXT,
  Country TEXT,
  Phone TEXT,
  Active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE Categories (
  CategoryId INTEGER PRIMARY KEY AUTOINCREMENT,
  CategoryName TEXT NOT NULL,
  Description TEXT
);

CREATE TABLE Products (
  ProductId INTEGER PRIMARY KEY AUTOINCREMENT,
  ProductName TEXT NOT NULL,
  SupplierId INTEGER NOT NULL,
  CategoryId INTEGER NOT NULL,
  UnitPrice REAL NOT NULL,
  UnitsInStock INTEGER NOT NULL DEFAULT 0,
  ReorderLevel INTEGER NOT NULL DEFAULT 10,
  Discontinued INTEGER NOT NULL DEFAULT 0,
  FOREIGN KEY (SupplierId) REFERENCES Suppliers(SupplierId),
  FOREIGN KEY (CategoryId) REFERENCES Categories(CategoryId)
);

CREATE TABLE Orders (
  OrderId INTEGER PRIMARY KEY AUTOINCREMENT,
  CustomerId INTEGER NOT NULL,
  EmployeeId INTEGER NOT NULL,
  OrderDate TEXT NOT NULL,
  RequiredDate TEXT,
  ShippedDate TEXT,
  ShipperId INTEGER,
  Freight REAL NOT NULL DEFAULT 0,
  ShipCountry TEXT,
  Status TEXT NOT NULL DEFAULT 'Open',
  RelatedProductIds TEXT,
  FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId),
  FOREIGN KEY (EmployeeId) REFERENCES Employees(EmployeeId),
  FOREIGN KEY (ShipperId) REFERENCES Shippers(ShipperId)
);

CREATE TABLE OrderDetails (
  OrderDetailId INTEGER PRIMARY KEY AUTOINCREMENT,
  OrderId INTEGER NOT NULL,
  ProductId INTEGER NOT NULL,
  UnitPrice REAL NOT NULL,
  Quantity INTEGER NOT NULL,
  Discount REAL NOT NULL DEFAULT 0,
  FOREIGN KEY (OrderId) REFERENCES Orders(OrderId),
  FOREIGN KEY (ProductId) REFERENCES Products(ProductId)
);

INSERT INTO Customers (CompanyName, ContactName, Country, City, Phone, Active) VALUES
('Alfreds Futterkiste', 'Maria Anders', 'Germany', 'Berlin', '030-0074321', 1),
('Around the Horn', 'Thomas Hardy', 'UK', 'London', '(171) 555-7788', 1),
('Berglunds snabbkop', 'Christina Berglund', 'Sweden', 'Lulea', '0921-12 34 65', 1),
('Bon app', 'Laurence Lebihan', 'France', 'Marseille', '91.24.45.40', 1),
('Eastern Connection', 'Ann Devon', 'USA', 'New York', '(212) 555-1234', 1),
('Island Trading', 'Helen Bennett', 'UK', 'Cowes', '(198) 555-8888', 1),
('Laughing Bacchus Winecellars', 'Yoshi Tannamuri', 'Canada', 'Vancouver', '(604) 555-3392', 1),
('Magazzini Alimentari Riuniti', 'Giovanni Rovelli', 'Italy', 'Bergamo', '035-640230', 1),
('Hanari Carnes', 'Mario Pontes', 'Brazil', 'Rio de Janeiro', '(21) 555-0091', 1),
('Legacy Customer', 'Retired Contact', 'USA', 'Seattle', '(206) 000-0000', 0);

INSERT INTO Employees (LastName, FirstName, Title, Region, HireDate) VALUES
('Davolio', 'Nancy', 'Sales Representative', 'WA', '2022-01-15'),
('Fuller', 'Andrew', 'Vice President, Sales', 'WA', '2020-03-10'),
('Leverling', 'Janet', 'Sales Representative', 'CA', '2021-08-01'),
('Peacock', 'Margaret', 'Sales Representative', 'TX', '2023-02-20'),
('Buchanan', 'Steven', 'Sales Manager', 'NY', '2019-11-12');

INSERT INTO Shippers (CompanyName, Phone) VALUES
('Speedy Express', '(503) 555-9831'),
('United Package', '(503) 555-3199'),
('Federal Shipping', '(503) 555-9931');

INSERT INTO Suppliers (CompanyName, ContactName, Country, Phone, Active) VALUES
('Exotic Liquids', 'Charlotte Cooper', 'UK', '(171) 555-2222', 1),
('New Orleans Cajun Delights', 'Shelley Burke', 'USA', '(100) 555-4822', 1),
('Grandma Kelly''s Homestead', 'Regina Murphy', 'USA', '(503) 555-8831', 1),
('Tokyo Traders', 'Yoshi Nagase', 'Japan', '(03) 3555-5011', 1),
('Old Supply Co.', 'Legacy Vendor', 'USA', '(206) 555-0000', 0);

INSERT INTO Categories (CategoryName, Description) VALUES
('Beverages', 'Soft drinks, coffees, teas, beers, and ales'),
('Condiments', 'Sweet and savory sauces, relishes, spreads, and seasonings'),
('Confections', 'Desserts, candies, and sweet breads'),
('Seafood', 'Seaweed and fish');

INSERT INTO Products (ProductName, SupplierId, CategoryId, UnitPrice, UnitsInStock, ReorderLevel, Discontinued) VALUES
('Chai', 1, 1, 18.00, 39, 10, 0),
('Chang', 1, 1, 19.00, 17, 25, 0),
('Aniseed Syrup', 1, 2, 10.00, 13, 15, 0),
('Chef Anton''s Cajun Seasoning', 2, 2, 22.00, 53, 10, 0),
('Grandma''s Boysenberry Spread', 3, 2, 25.00, 8, 15, 0),
('Ikura', 4, 4, 31.00, 4, 20, 0),
('Pavlova', 3, 3, 17.45, 29, 10, 0),
('Northwoods Cranberry Sauce', 3, 2, 40.00, 6, 12, 0),
('Mishi Kobe Niku', 4, 4, 97.00, 0, 5, 1),
('Sasquatch Ale', 2, 1, 14.00, 111, 25, 0);

INSERT INTO Orders (CustomerId, EmployeeId, OrderDate, RequiredDate, ShippedDate, ShipperId, Freight, ShipCountry, Status, RelatedProductIds) VALUES
(1, 1, '2026-01-03', '2026-01-17', '2026-01-10', 1, 32.38, 'Germany', 'Shipped', '1,4,7'),
(2, 3, '2026-01-05', '2026-01-19', NULL, 2, 11.61, 'UK', 'Open', '2,6'),
(5, 4, '2026-01-12', '2026-01-26', NULL, 3, 65.83, 'USA', 'Open', '4,8'),
(9, 2, '2026-01-16', '2026-01-30', '2026-01-28', 2, 41.34, 'Brazil', 'Shipped', '1,3,10'),
(7, 5, '2026-02-01', '2026-02-15', NULL, 1, 22.98, 'Canada', 'Delayed', '6,8'),
(4, 1, '2026-02-05', '2026-02-19', '2026-02-18', 3, 9.12, 'France', 'Shipped', '5,7'),
(6, 3, '2026-02-10', '2026-02-24', NULL, 2, 15.55, 'UK', 'Open', '2,3,4'),
(8, 2, '2026-02-15', '2026-03-01', NULL, 1, 77.21, 'Italy', 'Delayed', '6,10');

INSERT INTO OrderDetails (OrderId, ProductId, UnitPrice, Quantity, Discount) VALUES
(1, 1, 18.00, 10, 0.00),
(1, 4, 22.00, 6, 0.05),
(1, 7, 17.45, 5, 0.00),
(2, 2, 19.00, 24, 0.10),
(2, 6, 31.00, 3, 0.00),
(3, 4, 22.00, 40, 0.00),
(3, 8, 40.00, 12, 0.03),
(4, 1, 18.00, 20, 0.00),
(4, 3, 10.00, 15, 0.00),
(4, 10, 14.00, 30, 0.02),
(5, 6, 31.00, 8, 0.00),
(5, 8, 40.00, 5, 0.04),
(6, 5, 25.00, 10, 0.00),
(6, 7, 17.45, 15, 0.00),
(7, 2, 19.00, 18, 0.00),
(7, 3, 10.00, 22, 0.00),
(7, 4, 22.00, 10, 0.00),
(8, 6, 31.00, 7, 0.00),
(8, 10, 14.00, 25, 0.05);
