# 🍽️ LunchMate – Meal Planning & Ordering System

**VisionCoders — CSE 325 (2025)**

LunchMate is a web-based meal planning and ordering system designed to streamline the communication between cooks and customers.  
It allows cooks to publish daily menus, and enables customers to confirm or cancel their meal orders with ease.

This project was developed as part of the CSE 325 course and demonstrates full-stack application design using .NET, Blazor, and MongoDB.

---

## 🚀 Features Overview

### 👨‍🍳 **Cook Features**

- Create and manage meals (name, description, image, ingredients, price).
- Publish daily menus in the calendar.
- View customer orders filtered by date or meal.
- Manage inventory (ingredients, quantities, units, alerts).
- View customer list and their order history.
- Edit personal profile and password.

### 🧑‍💼 **Customer Features**

- Browse available meals with images.
- View weekly or monthly calendar menus.
- Confirm or cancel meal orders.
- Review full order history.
- Edit personal profile and password.

---

## 🧭 Application Navigation

The application includes the following interface sections:

### **Meals**

- Cook: add/edit/remove meals.
- Customer: view meals only.

### **Orders**

- Customer: confirm/cancel orders.
- Cook: filter and view all orders for the day.

### **Customers (Cook Only)**

- View full list of registered customers.
- Access customer details and order history.

### **Calendar**

- Weekly and monthly views.
- Status of each day: **Draft**, **Published**, **Closed**.
- Cooks can assign meals and publish menus.
- Customers can confirm/cancel orders only when the day is Published.

### **Inventory (Cook Only)**

- Add ingredients, track quantities, set low-stock alerts.

### **Profile**

- Update personal information and password.

### **Logout**

- Ends session and redirects to home screen.

---

## 📅 User Roles & Workflow

### **Cook Workflow**

1. Login
2. Create or update meals
3. Select a day in the calendar
4. Publish the menu
5. Review customer orders
6. Update inventory
7. Manage profile

### **Customer Workflow**

1. Login
2. Browse meals
3. Select the active day in the calendar
4. Confirm or cancel order
5. Review history
6. Manage profile

---

## 🛠️ Technologies Used

- **C# / .NET 10**
- **Blazor WebAssembly**
- **MongoDB**
- **HTML / CSS**
- **Railway (deployment target)**
- **GitHub Project Board for task management**

---

## 📄 Documentation

## 📄 User Documentation (PDF)

You can view the full user documentation here:

👉 [LunchMate User Documentation (PDF)](./Docs/LunchMate_User_Documentation.pdf)

### 🧩 **Code Documentation**

All services, models, and components include clear XML-style comments to explain functionality and logic.

---

## 📋 Project Board

This project includes an organized GitHub Project Board with tasks, milestones, and workflow:  
👉 _[Add link to GitHub Board here]_

---

## ▶️ How to Run the Project Locally

1. Clone the repository:
   https://github.com/AndreyBlanco/CSE325-visioncoders.git

LunchMate User Documentation v1.0 — December 2025
Created by: VisionCoders Team
Technical Writer:
-Andrey Blanco Alfaro
-Syla Marie Garzon Cumuyog
-Oscar Alejandro Moncada
